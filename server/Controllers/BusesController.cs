using System;
using System.Collections.Generic;
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

        private static string NormalizePlate(string plate) =>
            Regex.Replace(plate.ToUpperInvariant(), @"\s+", "").Trim();

        // Carry over the email-tracking fields only when the end date is unchanged,
        // so renewing a policy re-arms its reminder/expiry notifications.
        private static Policy BuildPolicy(Policy incoming, Policy? previous)
        {
            bool sameEndDate = previous != null && previous.EndDate == incoming.EndDate;
            return new Policy
            {
                StartDate = incoming.StartDate,
                EndDate = incoming.EndDate,
                LastEmailedDate = sameEndDate ? previous!.LastEmailedDate : null,
                LastNotifiedThreshold = sameEndDate ? previous!.LastNotifiedThreshold : null
            };
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
                Console.WriteLine($"[Buses] GetBuses failed: {ex}");
                return StatusCode(500, new { error = "Araç listesi okunamadı." });
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
                var validationError = ValidateBus(req.Plate, req.Brand, req.Operator, req.Policies);
                if (validationError != null) return BadRequest(new { error = validationError });

                var data = Db.ReadDb();

                var normalizedPlate = NormalizePlate(req.Plate);
                if (data.Buses.Any(b => NormalizePlate(b.Plate) == normalizedPlate))
                {
                    return BadRequest(new { error = "Bu plakaya sahip bir araç zaten kayıtlı!" });
                }

                var newBus = new Bus
                {
                    Id = "bus-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Plate = req.Plate.ToUpperInvariant().Trim(),
                    Brand = req.Brand.Trim(),
                    Operator = req.Operator.Trim(),
                    Policies = PolicyKeys.ToDictionary(
                        key => key,
                        key => BuildPolicy(req.Policies[key], null))
                };

                data.Buses.Insert(0, newBus);
                Db.WriteDb(data);

                return StatusCode(201, newBus);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Buses] CreateBus failed: {ex}");
                return StatusCode(500, new { error = "Araç eklenemedi." });
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

                // Validate everything up front so a bad row aborts the whole import.
                for (int i = 0; i < req.Count; i++)
                {
                    var busReq = req[i];
                    if (string.IsNullOrWhiteSpace(busReq.Plate)) continue;

                    var error = ValidateBus(busReq.Plate, busReq.Brand, busReq.Operator, busReq.Policies);
                    if (error != null)
                    {
                        return BadRequest(new { error = $"Satır {i + 2} ({busReq.Plate}): {error}" });
                    }
                }

                var data = Db.ReadDb();
                int importedCount = 0;
                int updatedCount = 0;

                foreach (var busReq in req)
                {
                    if (ValidateBus(busReq.Plate, busReq.Brand, busReq.Operator, busReq.Policies) != null)
                    {
                        continue; // Skip invalid/empty rows
                    }

                    var normalizedPlate = NormalizePlate(busReq.Plate);
                    var existingBusIndex = data.Buses.FindIndex(b => NormalizePlate(b.Plate) == normalizedPlate);
                    var existingBus = existingBusIndex != -1 ? data.Buses[existingBusIndex] : null;

                    var newPolicies = PolicyKeys.ToDictionary(
                        key => key,
                        key => BuildPolicy(busReq.Policies[key],
                            existingBus?.Policies.TryGetValue(key, out var op) == true ? op : null));

                    if (existingBus != null)
                    {
                        existingBus.Brand = busReq.Brand.Trim();
                        existingBus.Operator = busReq.Operator.Trim();
                        existingBus.Policies = newPolicies;
                        updatedCount++;
                    }
                    else
                    {
                        data.Buses.Insert(0, new Bus
                        {
                            Id = "bus-" + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + importedCount),
                            Plate = busReq.Plate.ToUpperInvariant().Trim(),
                            Brand = busReq.Brand.Trim(),
                            Operator = busReq.Operator.Trim(),
                            Policies = newPolicies
                        });
                        importedCount++;
                    }
                }

                Db.WriteDb(data);
                return Ok(new { success = true, importedCount, updatedCount });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Buses] BulkImport failed: {ex}");
                return StatusCode(500, new { error = "Araçlar içe aktarılamadı." });
            }
        }

        [HttpPut("{id}")]
        public IActionResult UpdateBus(string id, [FromBody] CreateBusRequest req)
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                var validationError = ValidateBus(req.Plate, req.Brand, req.Operator, req.Policies);
                if (validationError != null) return BadRequest(new { error = validationError });

                var data = Db.ReadDb();
                var busIndex = data.Buses.FindIndex(b => b.Id == id);
                if (busIndex == -1) return NotFound(new { error = "Araç bulunamadı." });

                var normalizedPlate = NormalizePlate(req.Plate);
                if (data.Buses.Any(b => b.Id != id && NormalizePlate(b.Plate) == normalizedPlate))
                {
                    return BadRequest(new { error = "Bu plakaya sahip başka bir araç zaten kayıtlı!" });
                }

                var oldBus = data.Buses[busIndex];
                var updatedBus = new Bus
                {
                    Id = oldBus.Id,
                    Plate = req.Plate.ToUpperInvariant().Trim(),
                    Brand = req.Brand.Trim(),
                    Operator = req.Operator.Trim(),
                    Policies = PolicyKeys.ToDictionary(
                        key => key,
                        key => BuildPolicy(req.Policies[key],
                            oldBus.Policies.TryGetValue(key, out var op) ? op : null))
                };

                data.Buses[busIndex] = updatedBus;
                Db.WriteDb(data);

                return Ok(updatedBus);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Buses] UpdateBus failed: {ex}");
                return StatusCode(500, new { error = "Araç güncellenemedi." });
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
                    return NotFound(new { error = "Araç bulunamadı." });
                }

                Db.WriteDb(data);
                return Ok(new { success = true, message = "Araç silindi." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Buses] DeleteBus failed: {ex}");
                return StatusCode(500, new { error = "Araç silinemedi." });
            }
        }
    }
}
