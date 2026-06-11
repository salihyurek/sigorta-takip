using System;
using System.Collections.Generic;
using System.Linq;
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

                var normalizedPlate = NormalizePlate(req.Plate);
                string? error = null;
                Bus? newBus = null;

                // Uniqueness check + insert in one atomic step so two concurrent creates
                // can't both slip past the duplicate-plate check.
                var ok = Db.Update(data =>
                {
                    if (data.Buses.Any(b => NormalizePlate(b.Plate) == normalizedPlate))
                    {
                        error = "Bu plakaya sahip bir araç zaten kayıtlı!";
                        return false;
                    }

                    newBus = new Bus
                    {
                        Id = "bus-" + Guid.NewGuid().ToString("n"),
                        Plate = req.Plate.ToUpperInvariant().Trim(),
                        Brand = req.Brand.Trim(),
                        Operator = req.Operator.Trim(),
                        Policies = PolicyKeys.ToDictionary(
                            key => key,
                            key => BuildPolicy(req.Policies[key], null))
                    };

                    data.Buses.Insert(0, newBus);
                    return true;
                });

                if (error != null) return BadRequest(new { error });
                if (!ok) return StatusCode(500, new { error = "Araç kaydedilemedi." });
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

                // Validate everything up front so a bad record aborts the whole import.
                // The client strips empty rows before sending, so we can't map an index
                // back to a spreadsheet row number — identify the offender by plate instead.
                for (int i = 0; i < req.Count; i++)
                {
                    var busReq = req[i];
                    if (string.IsNullOrWhiteSpace(busReq.Plate)) continue;

                    var error = ValidateBus(busReq.Plate, busReq.Brand, busReq.Operator, busReq.Policies);
                    if (error != null)
                    {
                        return BadRequest(new { error = $"{busReq.Plate}: {error}" });
                    }
                }

                int importedCount = 0;
                int updatedCount = 0;

                var ok = Db.Update(data =>
                {
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
                                Id = "bus-" + Guid.NewGuid().ToString("n"),
                                Plate = busReq.Plate.ToUpperInvariant().Trim(),
                                Brand = busReq.Brand.Trim(),
                                Operator = busReq.Operator.Trim(),
                                Policies = newPolicies
                            });
                            importedCount++;
                        }
                    }
                    return true;
                });

                if (!ok) return StatusCode(500, new { error = "Araçlar kaydedilemedi." });
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

                var normalizedPlate = NormalizePlate(req.Plate);
                bool notFound = false;
                string? error = null;
                Bus? updatedBus = null;

                var ok = Db.Update(data =>
                {
                    var busIndex = data.Buses.FindIndex(b => b.Id == id);
                    if (busIndex == -1) { notFound = true; return false; }

                    if (data.Buses.Any(b => b.Id != id && NormalizePlate(b.Plate) == normalizedPlate))
                    {
                        error = "Bu plakaya sahip başka bir araç zaten kayıtlı!";
                        return false;
                    }

                    var oldBus = data.Buses[busIndex];
                    updatedBus = new Bus
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
                    return true;
                });

                if (notFound) return NotFound(new { error = "Araç bulunamadı." });
                if (error != null) return BadRequest(new { error });
                if (!ok) return StatusCode(500, new { error = "Araç güncellenemedi." });
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
                bool found = false;
                var ok = Db.Update(data =>
                {
                    var initialLength = data.Buses.Count;
                    data.Buses = data.Buses.Where(b => b.Id != id).ToList();
                    found = data.Buses.Count != initialLength;
                    return found;
                });

                if (!found) return NotFound(new { error = "Araç bulunamadı." });
                if (!ok) return StatusCode(500, new { error = "Araç silinemedi." });
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
