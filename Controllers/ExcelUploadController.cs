using SignalTracker.Helper;
using SignalTracker.Models;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace SignalTracker.Controllers
{
    [Authorize]
    public class ExcelUploadController : BaseController
    {
        private readonly ApplicationDbContext db;
        private readonly CommonFunction cf;

        // Robust timezone: works on Windows ("India Standard Time") and Linux ("Asia/Kolkata")
        private static readonly TimeZoneInfo INDIAN_ZONE = GetIndianZone();
        private static TimeZoneInfo GetIndianZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
            catch { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        }

        public ExcelUploadController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            db = context;
            cf = new CommonFunction(context, httpContextAccessor);
        }

        public ActionResult Index()
        {
            if (!IsAngularRequest() || !cf.SessionCheck())
                return RedirectToAction("Index", "Home");
            return View();
        }

        [HttpGet]
        public IActionResult DownloadExcel(int FileType, string fileName)
        {
            var filePath = "";
            if (FileType == 0)
                filePath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedExcels", fileName);
            else
            {
                fileName = Constant.TempFiles[FileType];
                filePath = Path.Combine(Directory.GetCurrentDirectory(), "Template-Files", fileName);
            }

            if (System.IO.File.Exists(filePath))
            {
                var fileBytes = System.IO.File.ReadAllBytes(filePath);
                var contentType = CommonFunction.GetMimeType(filePath);
                return File(fileBytes, contentType, fileName);
            }
            else
                return Json(new { status = 0, message = "Template not found" });
        }

        [HttpGet]
        public async Task<IActionResult> GetUploadedExcelFiles(int fileType, CancellationToken ct = default)
        {
            try
            {
                var currentUserId = cf.UserId;
                bool filterByUser = currentUserId > 0;

                var query = from h in db.tbl_upload_history.AsNoTracking()
                            join u in db.tbl_user.AsNoTracking() on h.uploaded_by equals u.id into gu
                            from u in gu.DefaultIfEmpty()
                            where h.file_type == fileType
                            select new
                            {
                                id = h.id,
                                file_type = h.file_type,
                                file_name = h.file_name,
                                uploaded_on = h.uploaded_on,
                                uploaded_by = u != null ? u.name : null,
                                uploaded_id = h.uploaded_by,
                                status = h.status == 1 ? "Success" : "Failed",
                                remarks = h.remarks
                            };

                if (filterByUser)
                    query = query.Where(x => x.uploaded_id == currentUserId);

                var data = await query
                    .OrderByDescending(x => x.id)
                    .Take(20)
                    .ToListAsync(ct);

                return Ok(new { Status = 1, Data = data });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { Status = 0, Message = "Server error: " + ex.Message });
            }
        }

        // Increase request size here and in Program.cs (see note below)
        [HttpPost]
        [RequestSizeLimit(100_000_000)] // 100 MB
        public async Task<IActionResult> UploadExcelFile(
            [FromForm] string remarks,
            [FromForm] string token,
            [FromForm] string ip,
            [FromForm] string ProjectName,
            [FromForm] string SessionIds,
            [FromForm] int UploadFileType,
            [FromForm] IFormFile UploadFile,          // main CSV/ZIP
            [FromForm] IFormFile UploadNoteFile       // optional CSV/ZIP/GEOJSON
        )
        {
            var message = new ReturnAPIResponse();

            try
            {
                cf.SessionCheck();

                // token validation if needed
                // message = cf.MatchToken(token);
                message.Status = 1;

                if (message.Status != 1)
                    return Json(message);

                // sanitize & validate remarks
                var rv = InputValidator.ValidateRemarks(remarks ?? string.Empty, "Remarks");
                if (!rv.isValid)
                {
                    message.Status = 0;
                    message.Message = rv.errorMessage;
                    return Json(message);
                }
                remarks = rv.sanitized;

                if (UploadFile == null || UploadFile.Length == 0)
                {
                    message.Status = 0;
                    message.Message = "Please select excel file.";
                    return Json(message);
                }

                // --- Size > 0 (use float division) ---
                float sizeInKB = UploadFile.Length / 1024f;
                if (sizeInKB <= 0f)
                {
                    message.Status = 0;
                    message.Message = "File size should be greater than 0KB.";
                    return Json(message);
                }

                if (!IsCsvOrZip(UploadFile))
                {
                    message.Status = 0;
                    message.Message = "Please upload only CSV/ZIP file.";
                    return Json(message);
                }

                // Optional polygon file validations
                string inboundPolygonFile = string.Empty;
                if (UploadNoteFile != null && UploadNoteFile.Length > 0)
                {
                    float size2KB = UploadNoteFile.Length / 1024f;
                    if (size2KB <= 0f)
                    {
                        message.Status = 0;
                        message.Message = "Polygon file size should be greater than 0KB.";
                        return Json(message);
                    }

                    if (!IsCsvZipOrGeoJson(UploadNoteFile))
                    {
                        message.Status = 0;
                        message.Message = "Please upload valid inbound polygon file (CSV/ZIP/GEOJSON).";
                        return Json(message);
                    }
                }

                // --- Paths & names ---
                var directorypath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedExcels");
                Directory.CreateDirectory(directorypath); // safe if exists

                DateTime nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, INDIAN_ZONE);

                string mainExt = Path.GetExtension(UploadFile.FileName);
                string file_name = "File_" + nowIst.ToString("MMddyyyyHmmss") + mainExt;
                string mainPath = Path.Combine(directorypath, file_name);

                // Save main file (async)
                await using (var stream = System.IO.File.Create(mainPath))
                {
                    await UploadFile.CopyToAsync(stream);
                }

                string polygonPath = string.Empty;
                if (UploadNoteFile != null && UploadNoteFile.Length > 0)
                {
                    string ext2 = Path.GetExtension(UploadNoteFile.FileName);
                    inboundPolygonFile = "Polygon_" + nowIst.ToString("MMddyyyyHmmss") + ext2;
                    polygonPath = Path.Combine(directorypath, inboundPolygonFile);

                    await using (var stream = System.IO.File.Create(polygonPath))
                    {
                        await UploadNoteFile.CopyToAsync(stream);
                    }
                }

                // --- DB history record ---
                int userId = 0;
                if (HttpContext != null) userId = Convert.ToInt32(HttpContext.Session.GetInt32("UserID"));

                var excel_details = new tbl_upload_history
                {
                    remarks = remarks,
                    file_name = file_name,
                    polygon_file = inboundPolygonFile,
                    file_type = UploadFileType,
                    status = 1, // optimistic; will flip if process fails
                    uploaded_by = userId,
                    uploaded_on = nowIst
                };

                db.tbl_upload_history.Add(excel_details);
                db.SaveChanges();

                message.Status = 1;
                message.Message = "File has been uploaded successfully.";

                // If fileType == 2 -> create project
                int projectId = 0;
                if (UploadFileType == 2)
                {
                    var objProject = new tbl_project
                    {
                        project_name = ProjectName,
                        ref_session_id = SessionIds,
                        created_by_user_id = userId,
                        created_by_user_name = cf.UserName,
                        status = 1
                    };
                    db.tbl_project.Add(objProject);
                    db.SaveChanges();
                    projectId = objProject.id;
                }

                // --- Process the file ---
                string errorMsg = "";
                var csvProc = new ProcessCSVController(db, cf);
                bool ok = csvProc.Process(
                    excel_details.id,
                    mainPath,
                    UploadFile.FileName,
                    polygonPath,
                    UploadFileType,
                    projectId,
                    remarks,
                    out errorMsg
                );

                if (!ok)
                {
                    excel_details.status = 0;

                    if (projectId > 0)
                    {
                        var objProject = db.tbl_project.FirstOrDefault(a => a.id == projectId);
                        if (objProject != null)
                        {
                            objProject.status = 0;
                            db.Entry(objProject).State = EntityState.Modified;
                        }
                    }

                    message.Status = 0;
                    message.Message = errorMsg;
                }
                // else: keep success message (optionally append errorMsg if you use warnings)

                excel_details.errors = errorMsg;
                db.SaveChanges();

                return Json(message);
            }
            catch (Exception ex)
            {
                try
                {
                    new Writelog(db).write_exception_log(0, "AdminExcelUploadController", "UploadExcelFile", DateTime.Now, ex);
                }
                catch { /* swallow logging failure */ }

                return Json(new ReturnAPIResponse
                {
                    Status = 0,
                    Message = ex.InnerException != null ? ex.InnerException.Message : ex.Message
                });
            }
        }

        [HttpGet]
        public JsonResult GetSessions(DateTime fromDate, DateTime toDate)
        {
            var message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();

                var rawSessions = db.tbl_session
                    .Where(s => s.start_time >= fromDate && s.end_time <= toDate)
                    .Join(db.tbl_user,
                          s => s.user_id,
                          u => u.id,
                          (s, u) => new
                          {
                              s.id,
                              s.start_time,
                              s.notes,
                              s.start_address,
                              userName = u.name
                          })
                    .ToList();

                var formattedSessions = rawSessions.Select(x => new
                {
                    id = x.id,
                    label = $"{x.userName} {(x.start_time == null ? "" : x.start_time.Value.ToString("dd MMM yyyy hh:mm tt"))} {x.notes} {x.start_address}"
                }).ToList();

                message.Status = 1;
                message.Data = formattedSessions;
            }
            catch (Exception ex)
            {
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }

        // ----------------- Helpers -----------------

        // Allow by extension first (robust across browsers)
        private static bool IsCsvOrZip(IFormFile f)
        {
            if (f == null) return false;
            var ext = Path.GetExtension(f.FileName)?.ToLowerInvariant();
            if (ext == ".csv" || ext == ".zip") return true;

            // fallback on content-type (browsers vary)
            var ct = f.ContentType?.ToLowerInvariant();
            return ct == "text/csv"
                || ct == "application/vnd.ms-excel"
                || ct == "application/zip"
                || ct == "application/x-zip-compressed"
                || ct == "application/octet-stream";
        }

        private static bool IsCsvZipOrGeoJson(IFormFile f)
        {
            if (f == null) return false;
            var ext = Path.GetExtension(f.FileName)?.ToLowerInvariant();
            if (ext == ".csv" || ext == ".zip" || ext == ".geojson" || ext == ".json") return true;

            var ct = f.ContentType?.ToLowerInvariant();
            return ct == "application/geo+json"
                || ct == "application/json"
                || ct == "text/csv"
                || ct == "application/vnd.ms-excel"
                || ct == "application/zip"
                || ct == "application/x-zip-compressed"
                || ct == "application/octet-stream";
        }
    }
}