using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using SigortaTakip.Models;
using SigortaTakip.Services;

namespace SigortaTakip.Controllers
{
    [ApiController]
    [Route("api/buses")]
    public class BusesController : BaseApiController
    {
        public BusesController(DbService dbService, AuthService authService)
            : base(dbService, authService)
        {
        }

        private bool IsValidDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return false;
            var regex = new Regex(@"^\d{4}-\d{2}-\d{2}$");
            if (!regex.IsMatch(dateStr)) return false;
            
            return DateTime.TryParseExact(dateStr, "yyyy-MM-dd", 
                CultureInfo.InvariantCulture, 
                DateTimeStyles.None, out _);
        }

        [HttpGet]
        public IActionResult GetBuses()
        {
            var authCheck = CheckAuth();
            if (authCheck != null) return authCheck;

            try
            {
                var data = Db.ReadDb();
                return Ok(data.Buses ?? new List<Bus>());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to read buses", details = ex.Message });
            }
        }

        public class CreateBusRequest
        {
            public string Plate { get; set; } = "";
            public string Brand { get; set; } = "";
            public string Operator { get; set; } = "";
            public Dictionary<string, Policy> Policies { get; set; } = new();
        }

        [HttpPost]
        public IActionResult CreateBus([FromBody] CreateBusRequest req)
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                if (string.IsNullOrWhiteSpace(req.Plate) || 
                    string.IsNullOrWhiteSpace(req.Brand) || 
                    string.IsNullOrWhiteSpace(req.Operator) || 
                    req.Policies == null)
                {
                    return BadRequest(new { error = "All fields are required" });
                }

                // Validate dates
                foreach (var key in new[] { "trafik", "kasko", "koltuk" })
                {
                    if (!req.Policies.TryGetValue(key, out var policy) || 
                        policy == null || 
                        !IsValidDate(policy.StartDate) || 
                        !IsValidDate(policy.EndDate))
                    {
                        return BadRequest(new { error = $"Geçersiz tarih formatı: {key}" });
                    }
                }

                var data = Db.ReadDb();

                // Check if plate already exists
                var normalizedPlate = Regex.Replace(req.Plate.ToUpperInvariant(), @"\s+", "").Trim();
                var plateExists = data.Buses.Any(b => 
                    Regex.Replace(b.Plate.ToUpperInvariant(), @"\s+", "").Trim() == normalizedPlate);

                if (plateExists)
                {
                    return BadRequest(new { error = "Bu plakaya sahip bir araç zaten kayıtlı!" });
                }

                var newBus = new Bus
                {
                    Id = "bus-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Plate = req.Plate.ToUpperInvariant().Trim(),
                    Brand = req.Brand.Trim(),
                    Operator = req.Operator.Trim(),
                    Policies = new Dictionary<string, Policy>
                    {
                        { "trafik", new Policy { StartDate = req.Policies["trafik"].StartDate, EndDate = req.Policies["trafik"].EndDate, LastEmailedDate = null } },
                        { "kasko", new Policy { StartDate = req.Policies["kasko"].StartDate, EndDate = req.Policies["kasko"].EndDate, LastEmailedDate = null } },
                        { "koltuk", new Policy { StartDate = req.Policies["koltuk"].StartDate, EndDate = req.Policies["koltuk"].EndDate, LastEmailedDate = null } }
                    }
                };

                // Add to the beginning of the list
                data.Buses.Insert(0, newBus);
                Db.WriteDb(data);

                return StatusCode(201, newBus);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to add bus", details = ex.Message });
            }
        }

        [HttpPost("bulk-import")]
        public IActionResult BulkImport([FromBody] List<CreateBusRequest> req)
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                if (req == null || req.Count == 0)
                {
                    return BadRequest(new { error = "İçe aktarılacak araç verisi bulunamadı." });
                }

                // Check dates are valid first
                for (int i = 0; i < req.Count; i++)
                {
                    var busReq = req[i];
                    if (string.IsNullOrWhiteSpace(busReq.Plate)) continue;

                    foreach (var key in new[] { "trafik", "kasko", "koltuk" })
                    {
                        if (!busReq.Policies.TryGetValue(key, out var policy) || 
                            policy == null || 
                            !IsValidDate(policy.StartDate) || 
                            !IsValidDate(policy.EndDate))
                        {
                            return BadRequest(new { error = $"Satır {i + 2} ({busReq.Plate}): Geçersiz tarih formatı: {key}" });
                        }
                    }
                }

                var data = Db.ReadDb();
                int importedCount = 0;
                int updatedCount = 0;

                foreach (var busReq in req)
                {
                    if (string.IsNullOrWhiteSpace(busReq.Plate) || 
                        string.IsNullOrWhiteSpace(busReq.Brand) || 
                        string.IsNullOrWhiteSpace(busReq.Operator) || 
                        busReq.Policies == null)
                    {
                        continue; // Skip invalid rows
                    }

                    // Normalize plate
                    var normalizedPlate = Regex.Replace(busReq.Plate.ToUpperInvariant(), @"\s+", "").Trim();

                    // Check if plate already exists
                    var existingBusIndex = data.Buses.FindIndex(b => 
                        Regex.Replace(b.Plate.ToUpperInvariant(), @"\s+", "").Trim() == normalizedPlate);

                    var newPolicies = new Dictionary<string, Policy>();
                    foreach (var key in new[] { "trafik", "kasko", "koltuk" })
                    {
                        if (busReq.Policies.TryGetValue(key, out var policy) && policy != null)
                        {
                            newPolicies[key] = new Policy
                            {
                                StartDate = policy.StartDate,
                                EndDate = policy.EndDate,
                                LastEmailedDate = (existingBusIndex != -1 && data.Buses[existingBusIndex].Policies.TryGetValue(key, out var op) && op != null && op.EndDate == policy.EndDate)
                                    ? op.LastEmailedDate
                                    : null
                            };
                        }
                    }

                    if (existingBusIndex != -1)
                    {
                        // Update existing bus
                        var existingBus = data.Buses[existingBusIndex];
                        existingBus.Brand = busReq.Brand.Trim();
                        existingBus.Operator = busReq.Operator.Trim();
                        existingBus.Policies = newPolicies;
                        updatedCount++;
                    }
                    else
                    {
                        // Create new bus
                        var newBus = new Bus
                        {
                            Id = "bus-" + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + importedCount),
                            Plate = busReq.Plate.ToUpperInvariant().Trim(),
                            Brand = busReq.Brand.Trim(),
                            Operator = busReq.Operator.Trim(),
                            Policies = newPolicies
                        };
                        data.Buses.Insert(0, newBus);
                        importedCount++;
                    }
                }

                Db.WriteDb(data);
                return Ok(new { success = true, importedCount, updatedCount });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to bulk import buses", details = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public IActionResult UpdateBus(string id, [FromBody] CreateBusRequest req)
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                if (string.IsNullOrWhiteSpace(req.Plate) || 
                    string.IsNullOrWhiteSpace(req.Brand) || 
                    string.IsNullOrWhiteSpace(req.Operator) || 
                    req.Policies == null)
                {
                    return BadRequest(new { error = "All fields are required" });
                }

                // Validate dates
                foreach (var key in new[] { "trafik", "kasko", "koltuk" })
                {
                    if (!req.Policies.TryGetValue(key, out var policy) || 
                        policy == null || 
                        !IsValidDate(policy.StartDate) || 
                        !IsValidDate(policy.EndDate))
                    {
                        return BadRequest(new { error = $"Geçersiz tarih formatı: {key}" });
                    }
                }

                var data = Db.ReadDb();
                var busIndex = data.Buses.FindIndex(b => b.Id == id);

                if (busIndex == -1)
                {
                    return NotFound(new { error = "Bus not found" });
                }

                // Check if new plate clashes with another bus
                var normalizedPlate = Regex.Replace(req.Plate.ToUpperInvariant(), @"\s+", "").Trim();
                var plateExists = data.Buses.Any(b => 
                    b.Id != id && 
                    Regex.Replace(b.Plate.ToUpperInvariant(), @"\s+", "").Trim() == normalizedPlate);

                if (plateExists)
                {
                    return BadRequest(new { error = "Bu plakaya sahip başka bir araç zaten kayıtlı!" });
                }

                // Retain previous lastEmailedDate if the endDate hasn't changed, otherwise reset it
                var updatedPolicies = new Dictionary<string, Policy>();
                var oldBus = data.Buses[busIndex];

                foreach (var key in new[] { "trafik", "kasko", "koltuk" })
                {
                    var oldPolicy = oldBus.Policies.TryGetValue(key, out var op) ? op : null;
                    var newPolicy = req.Policies[key];

                    updatedPolicies[key] = new Policy
                    {
                        StartDate = newPolicy.StartDate,
                        EndDate = newPolicy.EndDate,
                        LastEmailedDate = (oldPolicy != null && oldPolicy.EndDate == newPolicy.EndDate) 
                            ? oldPolicy.LastEmailedDate 
                            : null
                    };
                }

                var updatedBus = new Bus
                {
                    Id = oldBus.Id,
                    Plate = req.Plate.ToUpperInvariant().Trim(),
                    Brand = req.Brand.Trim(),
                    Operator = req.Operator.Trim(),
                    Policies = updatedPolicies
                };

                data.Buses[busIndex] = updatedBus;
                Db.WriteDb(data);

                return Ok(updatedBus);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to update bus", details = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public IActionResult DeleteBus(string id)
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                var data = Db.ReadDb();
                var initialLength = data.Buses.Count;
                data.Buses = data.Buses.Where(b => b.Id != id).ToList();

                if (data.Buses.Count == initialLength)
                {
                    return NotFound(new { error = "Bus not found" });
                }

                Db.WriteDb(data);
                return Ok(new { success = true, message = "Bus deleted successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to delete bus", details = ex.Message });
            }
        }
    }
}
