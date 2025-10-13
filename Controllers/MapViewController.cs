using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using SignalTracker.Helper;
using SignalTracker.Models;

namespace SignalTracker.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MapViewController : BaseController
    {
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext db;
        private readonly CommonFunction cf;

        public MapViewController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor, IWebHostEnvironment env)
        {
            db = context;
            _env = env;
            cf = new CommonFunction(context, httpContextAccessor);
        }

        public class UserModel
        {
            public string name { get; set; }
            public string mobile { get; set; }
            public string make { get; set; }
            public string model { get; set; }
            public string os { get; set; }
            public string operator_name { get; set; }
            public string? device_id { get; set; }
            public string? gcm_id { get; set; }
            public int? company_id { get; set; }
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<JsonResult> user_signup([FromBody] UserModel model)
        {
            var message = new ReturnAPIResponse();

            try
            {
                if (model == null)
                {
                    message.Status = 0;
                    message.Message = "Invalid request.";
                    return Json(message);
                }

                if (!string.IsNullOrEmpty(model.device_id))
                {
                    var existingByDevice = await db.tbl_user
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.device_id == model.device_id);

                    if (existingByDevice != null)
                    {
                        message.Status = 1;
                        message.Message = "This device is already registered as - " + existingByDevice.name;
                        message.Data = new { userid = existingByDevice.id };
                        return Json(message); // early return to avoid duplicate creation
                    }
                }

                var existingUser = await db.tbl_user
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.mobile == model.mobile && u.make == model.make);

                if (existingUser != null)
                {
                    message.Status = 1;
                    message.Message = "User already exists.";
                    message.Data = new { userid = existingUser.id };
                    return Json(message);
                }

                var newUser = new tbl_user
                {
                    name = model.name,
                    mobile = model.mobile,
                    make = model.make,
                    model = model.model,
                    os = model.os,
                    operator_name = model.operator_name,
                    device_id = model.device_id,
                    gcm_id = model.gcm_id,
                    company_id = model.company_id
                };

                db.tbl_user.Add(newUser);
                await db.SaveChangesAsync();

                message.Status = 1;
                message.Message = "User saved successfully.";
                message.Data = new { userid = newUser.id };
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = "Error: " + ex.Message;
            }

            return Json(message);
        }

        public class SessionStartModel
        {
            public int userid { get; set; }
            public string start_time { get; set; }
            public string type { get; set; }
            public string? notes { get; set; }
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<JsonResult> start_session([FromBody] SessionStartModel model)
        {
            var message = new ReturnAPIResponse();

            try
            {
                var ci = CultureInfo.InvariantCulture;
                var newSess = new tbl_session
                {
                    user_id = model.userid,
                    start_time = DateTime.TryParse(model.start_time, ci, DateTimeStyles.RoundtripKind, out var ts) ? ts : (DateTime?)null,
                    type = model.type,
                    notes = model.notes
                };

                db.tbl_session.Add(newSess);
                await db.SaveChangesAsync();

                message.Status = 1;
                message.Message = "Session Started.";
                message.Data = new { sessionid = newSess.id };
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = "Error: " + ex.Message;
            }

            return Json(message);
        }

        public class SessionEndModel
        {
            public int sessionid { get; set; }
            public string end_time { get; set; }
            public string start_lat { get; set; }
            public string start_lon { get; set; }
            public string end_lat { get; set; }
            public string end_lon { get; set; }
            public float distance { get; set; }
            public int capture_frequency { get; set; }
            public string? start_address { get; set; }
            public string? end_address { get; set; }
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<JsonResult> end_session([FromBody] SessionEndModel model)
        {
            var message = new ReturnAPIResponse();

            try
            {
                var existingSession = await db.tbl_session.FirstOrDefaultAsync(u => u.id == model.sessionid);

                if (existingSession == null)
                {
                    message.Status = 0;
                    message.Message = "Session not found.";
                    return Json(message);
                }

                var ci = CultureInfo.InvariantCulture;

                existingSession.start_lat = float.TryParse(model.start_lat, NumberStyles.Float, ci, out var latVal) ? latVal : (float?)null;
                existingSession.start_lon = float.TryParse(model.start_lon, NumberStyles.Float, ci, out var lonVal) ? lonVal : (float?)null;
                existingSession.end_lat   = float.TryParse(model.end_lat,   NumberStyles.Float, ci, out var latVal1) ? latVal1 : (float?)null;
                existingSession.end_lon   = float.TryParse(model.end_lon,   NumberStyles.Float, ci, out var lonVal1) ? lonVal1 : (float?)null; // fixed
                existingSession.end_time  = DateTime.TryParse(model.end_time, ci, DateTimeStyles.RoundtripKind, out var ts) ? ts : (DateTime?)null;

                existingSession.start_address = model.start_address;
                existingSession.end_address = model.end_address;
                existingSession.capture_frequency = model.capture_frequency;
                existingSession.distance = model.distance;

                await db.SaveChangesAsync();

                message.Status = 1;
                message.Message = "Session Ended.";
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = "Error: " + ex.Message;
            }

            return Json(message);
        }

        [HttpGet]
        [Route("GetProjectPolygons")]
        public JsonResult GetProjectPolygons(int projectId)
        {
            var polygons = db.Set<PolygonDto>()
                .FromSqlRaw(@"
                    SELECT id, name, ST_AsText(region) as wkt 
                    FROM map_regions 
                    WHERE status = 1 and tbl_project_id = {0}", projectId)
                .AsNoTracking()
                .ToList();

            var result = polygons.Select(p => new
            {
                p.id,
                p.name,
                p.wkt
            });

            return Json(result);
        }

        public class NetworkLogFilters
        {
            public int SessionId { get; set; }
            public int? projectId { get; set; }
            public string token { get; set; }
            public DateTime? fromDate { get; set; }
            public DateTime? toDate { get; set; }
            public string providers { get; set; }
            public string technology { get; set; }
            public string metric { get; set; }
            public bool isBestTechnology { get; set; }
            public string Band { get; set; }
            public string EARFCN { get; set; }
            public string State { get; set; }
            public bool loadFilters { get; set; } = false;
        }

        public class MapFilter
        {
            public int session_id { get; set; }
            public string? NetworkType { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public int page { get; set; } = 1;
            public int limit { get; set; } = 1000;
        }

        [HttpGet]
        [Route("GetNetworkLog")]
        public async Task<JsonResult> GetNetworkLog([FromQuery] MapFilter filters)
        {
            if (filters.session_id <= 0)
            {
                return Json(new List<object>());
            }

            try
            {
                var limit = Math.Min(Math.Max(filters.limit, 1), 10000); // cap

                IQueryable<tbl_network_log> query = db.tbl_network_log.AsNoTracking()
                    .Where(log => log.session_id == filters.session_id);

                if (!string.IsNullOrEmpty(filters.NetworkType) && filters.NetworkType.ToUpper() != "ALL")
                {
                    query = query.Where(log => log.network == filters.NetworkType);
                }
                if (filters.StartDate.HasValue)
                {
                    query = query.Where(log => log.timestamp >= filters.StartDate.Value);
                }
                if (filters.EndDate.HasValue)
                {
                    var endDate = filters.EndDate.Value.AddDays(1);
                    query = query.Where(log => log.timestamp < endDate);
                }

                var paginatedQuery = query
                    .OrderBy(log => log.timestamp)
                    .Skip((filters.page - 1) * limit)
                    .Take(limit);

                var logs = await paginatedQuery
                    .Select(log => new
                    {
                        log.session_id,
                        log.lat,
                        log.lon,
                        log.rsrp,
                        log.rsrq,
                        log.sinr,
                        log.network,
                        log.band,
                        log.timestamp,
                        log.dl_tpt,
                        log.ul_tpt,
                        m_alpha_long = log.m_alpha_long,   // keep original
                        provider = log.m_alpha_long,       // alias for convenience
                        log.mos,
                        log.volte_call,
                        log.image_path
                    })
                    .ToListAsync();

                return Json(logs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetNetworkLog: {ex.Message}");
                return new JsonResult(new { message = "An error occurred on the server." }) { StatusCode = 500 };
            }
        }

        [HttpGet]
        [Route("GetPredictionLog")]
        public JsonResult GetPredictionLog(
            int? projectId, string token, DateTime? fromDate, DateTime? toDate,
            string providers, string technology, string metric,
            bool isBestTechnology, string Band, string EARFCN, string State,
            int pointsInsideBuilding = 0, bool loadFilters = false)
        {
            var message = new ReturnAPIResponse();

            try
            {
                cf.SessionCheck();

                IQueryable<tbl_prediction_data> query = db.tbl_prediction_data.AsNoTracking();

                if (projectId.HasValue && projectId != 0)
                {
                    query = query.Where(e => e.tbl_project_id == projectId);
                }

                if (!string.IsNullOrEmpty(Band))
                    query = query.Where(e => e.band == Band);

                if (!string.IsNullOrEmpty(EARFCN))
                    query = query.Where(e => e.earfcn == EARFCN);

                // Optional: filter by date/provider/tech if needed here...

                // Normalize metric key
                var metricKey = (metric ?? "RSRP").ToUpperInvariant();

                // Prepare data source
                List<(double? lat, double? lon, double? rsrp, double? rsrq, double? sinr)> dataSrc;

                if (pointsInsideBuilding == 1 && projectId.HasValue)
                {
                    // Raw SQL spatial containment
                    string sqlQuery = @"
                        SELECT
                            tpd.tbl_project_id,
                            tpd.lat,
                            tpd.lon,
                            tpd.rsrp,
                            tpd.rsrq,
                            tpd.sinr,
                            tpd.band,
                            tpd.earfcn
                        FROM
                            tbl_prediction_data AS tpd
                        JOIN
                            map_regions AS mr ON tpd.tbl_project_id = mr.tbl_project_id
                        WHERE
                            tpd.tbl_project_id = {0}
                            AND ST_Contains(mr.region, ST_PointFromText(CONCAT('POINT(', tpd.lon, ' ', tpd.lat, ')'), 4326));";

                    var matchingPoints = db.Set<PredictionPointDto>()
                        .FromSqlRaw(sqlQuery, projectId.Value)
                        .AsNoTracking()
                        .ToList();

                    dataSrc = matchingPoints
                        .Select(a => ((double?)a.lat, (double?)a.lon, (double?)a.rsrp, (double?)a.rsrq, (double?)a.sinr))
                        .ToList();
                }
                else
                {
                    var baseRows = query.Select(a => new { a.lat, a.lon, a.rsrp, a.rsrq, a.sinr }).ToList();
                    dataSrc = baseRows
                        .Select(a => ((double?)a.lat, (double?)a.lon, (double?)a.rsrp, (double?)a.rsrq, (double?)a.sinr))
                        .ToList();
                }

                var dataList = dataSrc.Select(a => new
                {
                    lat = a.lat,
                    lon = a.lon,
                    prm = metricKey == "RSRP" ? a.rsrp : (metricKey == "RSRQ" ? a.rsrq : a.sinr)
                }).ToList();

                // Averages from the same dataset
                double? averageRsrp = dataSrc.Where(x => x.rsrp.HasValue).Average(x => (double?)x.rsrp.Value);
                double? averageRsrq = dataSrc.Where(x => x.rsrq.HasValue).Average(x => (double?)x.rsrq.Value);
                double? averageSinr = dataSrc.Where(x => x.sinr.HasValue).Average(x => (double?)x.sinr.Value);

                // Threshold/color settings for graph
                GraphStruct CoveragePerfGraph = new GraphStruct();
                var setting = db.thresholds.AsNoTracking().FirstOrDefault(x => x.user_id == cf.UserId) 
                              ?? db.thresholds.AsNoTracking().FirstOrDefault(x => x.is_default == 1);

                List<SettingReangeColor>? settingObj = null;
                if (setting != null && dataList.Count > 0)
                {
                    if (metricKey == "RSRP")
                        settingObj = JsonConvert.DeserializeObject<List<SettingReangeColor>>(setting.rsrp_json);
                    else if (metricKey == "RSRQ")
                        settingObj = JsonConvert.DeserializeObject<List<SettingReangeColor>>(setting.rsrq_json);
                    else if (metricKey == "SINR") // corrected from "SNR"
                        settingObj = JsonConvert.DeserializeObject<List<SettingReangeColor>>(setting.sinr_json);

                    if (settingObj != null && settingObj.Count > 0)
                    {
                        int totalCount = dataList.Count;
                        GrapSeries seriesObj = new GrapSeries();
                        foreach (var s in settingObj)
                        {
                            CoveragePerfGraph.Category.Add(s.range);
                            int matchedCount = dataList.Count(a => a.prm >= s.min && a.prm <= s.max);
                            float per = totalCount > 0 ? (matchedCount * 100f / totalCount) : 0f;
                            seriesObj.data.Add(new { y = Math.Round(per, 2), color = s.color });
                        }
                        CoveragePerfGraph.series.Add(seriesObj);
                    }
                }

                message.Status = 1;
                message.Data = new
                {
                    dataList,
                    avgRsrp = averageRsrp,
                    avgRsrq = averageRsrq,
                    avgSinr = averageSinr,
                    colorSetting = settingObj,
                    coveragePerfGraph = CoveragePerfGraph,
                };
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }

            return Json(message);
        }

        [HttpGet]
        [Route("GetPredictionDataForSelectedBuildingPolygonsRaw")]
        public JsonResult GetPredictionDataForSelectedBuildingPolygonsRaw(int projectId, string metric)
        {
            try
            {
                string sqlQuery = @"
                    SELECT
                        tpd.tbl_project_id,
                        tpd.lat,
                        tpd.lon,
                        tpd.rsrp,
                        tpd.rsrq,
                        tpd.sinr,
                        tpd.band,
                        tpd.earfcn
                    FROM
                        tbl_prediction_data AS tpd
                    JOIN
                        map_regions AS mr ON tpd.tbl_project_id = mr.tbl_project_id
                    WHERE
                        tpd.tbl_project_id = {0}
                        AND ST_Contains(mr.region, ST_PointFromText(CONCAT('POINT(', tpd.lon, ' ', tpd.lat, ')'), 4326));";

                var matchingPoints = db.Set<PredictionPointDto>()
                    .FromSqlRaw(sqlQuery, projectId)
                    .AsNoTracking()
                    .ToList();

                var metricKey = (metric ?? "RSRP").ToUpperInvariant();

                var data = matchingPoints.Select(a => new
                {
                    a.lat,
                    a.lon,
                    Prm = metricKey == "RSRP" ? a.rsrp : (metricKey == "RSRQ" ? a.rsrq : a.sinr)
                }).ToList();

                return Json(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching prediction data with raw SQL: {ex.Message}");
                return Json(new { error = "An error occurred while fetching data.", details = ex.Message });
            }
        }

        [HttpGet]
        [Route("GetProjects")]
        public JsonResult GetProjects()
        {
            var message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();

                message.Status = 1;
                message.Data = db.tbl_project.AsNoTracking().Select(a => new
                {
                    a.id,
                    a.project_name,
                    a.from_date,
                    a.to_date,
                    a.provider,
                    a.tech,
                    a.band,
                    a.earfcn,
                    a.apps,
                    a.created_on
                }).ToList();
            }
            catch (Exception ex)
            {
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> UploadImage([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            var allowedExts = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext) || !allowedExts.Contains(ext.ToLowerInvariant()))
            {
                return BadRequest("Only image files are allowed.");
            }

            var webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var uploadFolder = Path.Combine(webRootPath, "uploaded_images");

            if (!Directory.Exists(uploadFolder))
                Directory.CreateDirectory(uploadFolder);

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadFolder, fileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var publicUrl = $"/uploaded_images/{fileName}";
            return Ok(new { message = "Image uploaded successfully.", filename = fileName, url = publicUrl });
        }

        public class LogFilterModel
        {
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public string? Provider { get; set; }
            public int? PolygonId { get; set; }
        }

        [HttpGet]
        [Route("GetLogsByDateRange")]
        public async Task<JsonResult> GetLogsByDateRange([FromQuery] LogFilterModel filters)
        {
            try
            {
                IQueryable<tbl_network_log> query = db.tbl_network_log.AsNoTracking();

                if (filters.StartDate.HasValue)
                {
                    query = query.Where(log => log.timestamp >= filters.StartDate.Value);
                }

                if (filters.EndDate.HasValue)
                {
                    var endDate = filters.EndDate.Value.AddDays(1);
                    query = query.Where(log => log.timestamp < endDate);
                }

                if (!string.IsNullOrEmpty(filters.Provider))
                {
                    query = query.Where(log => log.m_alpha_long == filters.Provider);
                }

                if (filters.PolygonId.HasValue)
                {
                    query = query.Where(log => log.polygon_id == filters.PolygonId.Value);
                }

                var logs = await query
                    .OrderBy(log => log.timestamp)
                    .Take(20000)
                    .Select(log => new
                    {
                        log.session_id,
                        log.lat,
                        log.lon,
                        log.rsrp,
                        log.rsrq,
                        log.sinr,
                        log.network,
                        log.band,
                        log.timestamp,
                        provider = log.m_alpha_long,
                        log.dl_tpt,
                        log.ul_tpt,
                        log.mos,
                        log.polygon_id,
                        log.image_path
                    })
                    .ToListAsync();

                    

                if (!logs.Any())
                {
                    return Json(new List<object>());
                }

                return Json(logs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLogsByDateRange: {ex.Message}");
                return new JsonResult(new { message = "An error occurred on the server." }) { StatusCode = 500 };
            }
        }

        public class NetworkLogPostModel
        {
            [JsonPropertyName("sessionid")]
            public int sessionid { get; set; }

            [JsonPropertyName("data")]
            public List<log_network> data { get; set; }
        }

        [HttpGet]
        [Route("GetProviders")]
        public JsonResult GetProviders()
        {
            var providerNames = db.tbl_network_log.AsNoTracking()
                .Where(p => !string.IsNullOrEmpty(p.m_alpha_long))
                .Select(p => p.m_alpha_long)
                .Distinct()
                .ToList();

            var providers = providerNames
                .Select((name, index) => new { id = index + 1, name })
                .ToList();

            return Json(providers);
        }

        [HttpGet]
        [Route("GetTechnologies")]
        public JsonResult GetTechnologies()
        {
            var technologyNames = db.tbl_network_log.AsNoTracking()
                .Where(t => !string.IsNullOrEmpty(t.network))
                .Select(t => t.network)
                .Distinct()
                .ToList();

            var technologies = technologyNames
                .Select((name, index) => new { id = name, name })
                .ToList();

            return Json(technologies);
        }

        [HttpGet]
        [Route("GetBands")]
        public JsonResult GetBands()
        {
            try
            {
                var bandNames = db.tbl_network_log.AsNoTracking()
                    .Where(b => !string.IsNullOrEmpty(b.band))
                    .Select(b => b.band)
                    .Distinct()
                    .ToList();

                var bands = bandNames
                    .Select((name, index) => new { id = index + 1, name })
                    .ToList();

                return Json(bands);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetBands: {ex.Message}");
                return new JsonResult(new
                {
                    status = 0,
                    message = "Error fetching bands data",
                    error = ex.Message
                })
                { StatusCode = 500 };
            }
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<JsonResult> log_networkAsync([FromBody] NetworkLogPostModel model)
        {
            var message = new ReturnMessage();
            var ci = CultureInfo.InvariantCulture;

            try
            {
                if (model?.data == null || !model.data.Any())
                {
                    message.Status = 0;
                    message.Message = "No data received.";
                    return Json(message);
                }

                var logsToInsert = new List<tbl_network_log>(model.data.Count);

                foreach (var item in model.data)
                {
                    var log = new tbl_network_log
                    {
                        session_id = model.sessionid,
                        timestamp = DateTime.TryParse(item.timestamp, ci, DateTimeStyles.RoundtripKind, out var ts) ? ts : (DateTime?)null,
                        lat = float.TryParse(item.lat, NumberStyles.Float, ci, out var latVal) ? latVal : (float?)null,
                        lon = float.TryParse(item.lon, NumberStyles.Float, ci, out var lonVal) ? lonVal : (float?)null,
                        battery = int.TryParse(item.battery, out var batVal) ? batVal : (int?)null,
                        dls = item.dls,
                        uls = item.uls,
                        call_state = item.call_state,
                        hotspot = item.hotspot,
                        apps = item.apps,
                        num_cells = int.TryParse(item.num_cells, out var ncVal) ? ncVal : (int?)null,
                        network = item.network,
                        m_mcc = int.TryParse(item.m_mcc, out var mccVal) ? mccVal : (int?)null,
                        m_mnc = int.TryParse(item.m_mnc, out var mncVal) ? mncVal : (int?)null,
                        m_alpha_long = item.m_alpha_long,
                        m_alpha_short = item.m_alpha_short,
                        mci = item.mci,
                        pci = item.pci,
                        tac = item.tac,
                        earfcn = item.earfcn,
                        rssi = float.TryParse(item.rssi, NumberStyles.Float, ci, out var rssiVal) ? rssiVal : (float?)null,
                        rsrp = float.TryParse(item.rsrp, NumberStyles.Float, ci, out var rsrpVal) ? rsrpVal : (float?)null,
                        rsrq = float.TryParse(item.rsrq, NumberStyles.Float, ci, out var rsrqVal) ? rsrqVal : (float?)null,
                        sinr = float.TryParse(item.sinr, NumberStyles.Float, ci, out var sinrVal) ? sinrVal : (float?)null,
                        total_rx_kb = item.total_rx_kb,
                        total_tx_kb = item.total_tx_kb,
                        mos = float.TryParse(item.mos, NumberStyles.Float, ci, out var mosVal) ? mosVal : (float?)null,
                        jitter = float.TryParse(item.jitter, NumberStyles.Float, ci, out var jitterVal) ? jitterVal : (float?)null,
                        latency = float.TryParse(item.latency, NumberStyles.Float, ci, out var latnVal) ? latnVal : (float?)null,
                        packet_loss = float.TryParse(item.packet_loss, NumberStyles.Float, ci, out var lossVal) ? lossVal : (float?)null,
                        dl_tpt = item.dl_tpt,
                        ul_tpt = item.ul_tpt,
                        volte_call = item.volte_call,
                        band = item.band,
                        cqi = float.TryParse(item.cqi, NumberStyles.Float, ci, out var cqiVal) ? cqiVal : (float?)null,
                        bler = item.bler,
                        primary_cell_info_1 = item.primary_cell_info_1,
                        primary_cell_info_2 = item.primary_cell_info_2,
                        all_neigbor_cell_info = item.all_neigbor_cell_info,
                        image_path = item.image_path,
                    };

                    // Spatial lookup only if coordinates are present
                    if (log.lat.HasValue && log.lon.HasValue)
                    {
                        int srid = 4326;
                        string pointWKT = FormattableString.Invariant($"POINT({log.lon.Value} {log.lat.Value})");

                        var polygonId = await db.PolygonMatches
                            .FromSqlInterpolated($@"
                                SELECT id FROM map_regions 
                                WHERE ST_Contains(region, ST_GeomFromText({pointWKT}, {srid})) 
                                LIMIT 1")
                            .AsNoTracking()
                            .Select(p => (int?)p.id)
                            .FirstOrDefaultAsync();

                        log.polygon_id = polygonId;
                    }

                    logsToInsert.Add(log);
                }

                await db.tbl_network_log.AddRangeAsync(logsToInsert);
                await db.SaveChangesAsync();

                message.Status = 1;
                message.Message = "Data saved successfully.";
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = "Error: " + ex.Message;
            }

            return Json(message);
        }
    }
}