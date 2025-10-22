using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SignalTracker.Helper;
using SignalTracker.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

namespace SignalTracker.Controllers
{
    [Route("Admin/[action]")]
    public class AdminController : BaseController
    {
        private readonly ApplicationDbContext db;
        private readonly CommonFunction cf;

        public AdminController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            db = context;
            cf = new CommonFunction(context, httpContextAccessor);
        }

        public IActionResult Index()
        {
            if (!cf.SessionCheck())
                return RedirectToAction("Index", "Home");
            return View();
        }

        public IActionResult Dashboard()
        {
            if (!IsAngularRequest() || !cf.SessionCheck())
            {
                return RedirectToAction("Index", "Home");
            }
            ViewBag.UserType = cf.UserType;
            return View();
        }

        // Helper: Check if a MySQL index exists on the current DB
        private async Task<bool> MySqlIndexExistsAsync(string table, string indexName)
        {
            var conn = db.Database.GetDbConnection();
            var shouldClose = false;
            try
            {
                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync();
                    shouldClose = true;
                }

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = @table
                      AND INDEX_NAME = @index
                ";
                var p1 = cmd.CreateParameter(); p1.ParameterName = "@table"; p1.Value = table; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@index"; p2.Value = indexName; cmd.Parameters.Add(p2);

                var result = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                return result > 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (shouldClose && conn.State == ConnectionState.Open)
                    await conn.CloseAsync();
            }
        }

        // Helper: Prefer using the user_id index explicitly for COUNT(DISTINCT user_id) if available
        private async Task<int> CountDistinctUsersWithIndexHintIfAvailableAsync()
        {
            if (await MySqlIndexExistsAsync("tbl_session", "user_id"))
            {
                var conn = db.Database.GetDbConnection();
                var shouldClose = false;
                try
                {
                    if (conn.State != ConnectionState.Open)
                    {
                        await conn.OpenAsync();
                        shouldClose = true;
                    }

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(DISTINCT `user_id`) FROM `tbl_session` USE INDEX (`user_id`);";
                    var result = await cmd.ExecuteScalarAsync();
                    return Convert.ToInt32(result);
                }
                catch
                {
                    // fall back below
                }
                finally
                {
                    if (shouldClose && conn.State == ConnectionState.Open)
                        await conn.CloseAsync();
                }
            }

            return await db.tbl_session
                .AsNoTracking()
                .Select(s => s.user_id)
                .Distinct()
                .CountAsync();
        }

        [HttpGet]
        public async Task<JsonResult> GetReactDashboardData()
        {
            var message = new ReturnAPIResponse();
            try
            {
                // Security note: uncomment for production
                // if (!cf.SessionCheck()) { message.Status = 0; message.Message = "Unauthorized"; return Json(message); }

                message.Status = 1;

                var today = DateTime.Today;

                // Top-level stats
                int totalSessions = await db.tbl_session.AsNoTracking().CountAsync();
                int totalOnlineSessions = await db.tbl_session.AsNoTracking().CountAsync(s =>s.start_time !=null && s.end_time == null && s.start_time.Value.Date== today);
                int totalSamples = await db.tbl_network_log.AsNoTracking().CountAsync();
                int totalUsers = await CountDistinctUsersWithIndexHintIfAvailableAsync();

                // Monthly Samples (format month string after materialization)
                var monthly = await db.tbl_network_log
                    .AsNoTracking()
                    .Where(n => n.timestamp.HasValue)
                    .GroupBy(n => new { n.timestamp.Value.Year, n.timestamp.Value.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                    .OrderBy(x => x.Year).ThenBy(x => x.Month)
                    .ToListAsync();

                var monthlySampleCounts = monthly
                    .Select(x => new { month = $"{x.Year:D4}-{x.Month:D2}", count = x.Count })
                    .ToList();

                // Operator wise Samples
                var operatorWiseSamples = await db.tbl_network_log
                    .AsNoTracking()
                    .Where(a => !string.IsNullOrEmpty(a.m_alpha_long))
                    .GroupBy(n => n.m_alpha_long)
                    .Select(g => new { name = g.Key, value = g.Count() })
                    .OrderByDescending(x => x.value)
                    .ToListAsync();

                // Network Type Distribution
                var networkTypeDistribution = await db.tbl_network_log
                    .AsNoTracking()
                    .Where(n => !string.IsNullOrEmpty(n.network))
                    .GroupBy(n => n.network)
                    .Select(g => new { name = g.Key, value = g.Count() })
                    .OrderByDescending(x => x.value)
                    .ToListAsync();

                // Average RSRP Per Operator
                var avgRsrpPerOperator = await db.tbl_network_log
                    .AsNoTracking()
                    .Where(n => !string.IsNullOrEmpty(n.m_alpha_long) && n.rsrp.HasValue)
                    .GroupBy(n => n.m_alpha_long)
                    .Select(g => new { name = g.Key, value = Math.Round(g.Average(item => item.rsrp.Value), 2) })
                    .OrderByDescending(x => x.value)
                    .ToListAsync();

                // Band Distribution
                var bandRows = await db.tbl_network_log
                    .AsNoTracking()
                    .Where(n => !string.IsNullOrEmpty(n.band))
                    .GroupBy(n => n.band)
                    .Select(g => new { Band = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .ToListAsync();

                var bandDistribution = bandRows
                    .Select(x => new { name = "Band " + x.Band, value = x.Count })
                    .ToList();

                // Handset wise Distribution
                var handsetDistribution = await (
                    from user in db.tbl_user.AsNoTracking()
                    join session in db.tbl_session.AsNoTracking() on user.id equals session.user_id
                    where !string.IsNullOrEmpty(user.make)
                    group user by user.make into g
                    select new { name = g.Key, value = g.Count() }
                ).ToListAsync();

                message.Data = new
                {
                    totalSessions,
                    totalOnlineSessions,
                    totalSamples,
                    totalUsers,
                    monthlySampleCounts,
                    operatorWiseSamples,
                    networkTypeDistribution,
                    avgRsrpPerOperator,
                    bandDistribution,
                    handsetDistribution
                };
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = "An error occurred while fetching dashboard data: " + ex.Message;
            }

            return Json(message);
        }

        [HttpGet]
        public async Task<JsonResult> GetDashboardData_old()
        {
            var message = new ReturnAPIResponse();

            try
            {
                cf.SessionCheck();
                message.Status = 1;

                var today = DateTime.Today;

                int totalSessions = await db.tbl_session.AsNoTracking().CountAsync();

                int totalOnlineSessions = await db.tbl_session.AsNoTracking()
                    .Where(s => s.start_time != null && s.end_time == null && s.start_time.Value.Date == today)
                    .CountAsync();

                int totalSamples = await db.tbl_network_log.AsNoTracking().CountAsync();

                int totalUsers = await db.tbl_session.AsNoTracking()
                    .Select(s => s.user_id)
                    .Distinct()
                    .CountAsync();

                int totalNetworkTypes = await db.tbl_network_log.AsNoTracking()
                    .Where(x => x.network != null && x.network != "")
                    .Select(x => x.network)
                    .Distinct()
                    .CountAsync();

                var networkTypeDistribution_horizontal_bar = await db.tbl_network_log.AsNoTracking()
                    .Where(x => x.network != null && x.network != "")
                    .GroupBy(x => x.network)
                    .Select(g => new { network = g.Key, count = g.Count() })
                    .ToListAsync();

                var samplesByAlphaLong = await db.tbl_network_log.AsNoTracking()
                    .Where(a => a.m_alpha_long != null && a.m_alpha_long != "")
                    .GroupBy(n => n.m_alpha_long)
                    .Select(g => new { m_alpha_long = g.Key, count = g.Count() })
                    .ToListAsync();

                DateTime sixMonthsAgo = today.AddMonths(-5);

                var monthly = await db.tbl_network_log.AsNoTracking()
                    .Where(n => n.timestamp >= sixMonthsAgo)
                    .GroupBy(n => new { n.timestamp.Value.Year, n.timestamp.Value.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .Select(g => new { Year = g.Key.Year, Month = g.Key.Month, Count = g.Count() })
                    .ToListAsync();

                var monthlySampleCounts = monthly
                    .Select(x => new
                    {
                        month = new DateTime(x.Year, x.Month, 1).ToString("yyyy-MM"),
                        count = x.Count
                    }).ToList();

                var avgRsrpSinrPerOperator_bar = await db.tbl_network_log.AsNoTracking()
                    .Where(x => x.rsrp != null && x.sinr != null && x.m_alpha_long != null)
                    .GroupBy(x => x.m_alpha_long)
                    .Select(g => new
                    {
                        Operator = g.Key,
                        AvgRSRP = Math.Round(g.Average(x => x.rsrp.Value), 2),
                        AvgSINR = Math.Round(g.Average(x => x.sinr.Value), 2)
                    })
                    .ToListAsync();

                var bandDistribution_pie = await db.tbl_network_log.AsNoTracking()
                    .Where(x => !string.IsNullOrEmpty(x.band))
                    .GroupBy(x => x.band)
                    .Select(g => new { band = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToListAsync();

                message.Data = new
                {
                    totalSessions,
                    totalOnlineSessions,
                    totalSamples,
                    totalUsers,
                    totalNetworkTypes,
                    networkTypeDistribution_horizontal_bar,
                    samplesByAlphaLong,
                    monthlySampleCounts,
                    avgRsrpSinrPerOperator_bar,
                    bandDistribution_pie
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
        public async Task<JsonResult> GetDashboardGraphData()
        {
            var message = new ReturnAPIResponse();

            try
            {
                cf.SessionCheck();
                message.Status = 1;

                var networkTypeDistribution_horizontal_bar = await db.tbl_network_log.AsNoTracking()
                    .Where(x => x.network != null && x.network != "")
                    .GroupBy(x => x.network)
                    .Select(g => new { network = g.Key, count = g.Count() })
                    .ToListAsync();

                var avgRsrpSinrPerOperator_bar = await db.tbl_network_log.AsNoTracking()
                    .Where(x => x.rsrp.HasValue && x.sinr.HasValue && x.m_alpha_long != null)
                    .GroupBy(x => x.m_alpha_long)
                    .Select(g => new
                    {
                        Operator = g.Key,
                        AvgRSRP = Math.Round(g.Average(x => x.rsrp.Value), 2)
                    })
                    .OrderByDescending(x => x.AvgRSRP)
                    .ToListAsync();

                var bandDistribution_pie = await db.tbl_network_log.AsNoTracking()
                    .Where(x => !string.IsNullOrEmpty(x.band))
                    .GroupBy(x => x.band)
                    .Select(g => new { band = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToListAsync();

                // Handset-wise average RSRP (by make), computed over ALL logs belonging to users with that make
                var handsetWiseAvg_bar = await (
                    from log in db.tbl_network_log.AsNoTracking()
                    join s in db.tbl_session.AsNoTracking() on log.session_id equals s.id
                    join u in db.tbl_user.AsNoTracking() on s.user_id equals u.id
                    where log.rsrp.HasValue && !string.IsNullOrEmpty(u.make)
                    group log by u.make into g
                    select new
                    {
                        Make = g.Key,
                        Avg = Math.Round(g.Average(x => x.rsrp.Value), 2), // average over logs.rsrp
                        Samples = g.Count()
                    }
                ).OrderByDescending(x => x.Avg).ToListAsync();

                message.Data = new
                {
                    networkTypeDistribution_horizontal_bar,
                    avgRsrpSinrPerOperator_bar,
                    bandDistribution_pie,
                    handsetWiseAvg_bar
                };
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }

            return Json(message);
        }

        [HttpPost]
        public async Task<JsonResult> GetAllUsers()
        {
            try
            {
                var users = await db.tbl_user.AsNoTracking()
                    .Where(a => a.isactive == 1)
                    .OrderBy(a => a.name)
                    .Select(u => new
                    {
                        u.id,
                        u.name,
                        u.email,
                        u.mobile,
                        u.m_user_type_id,
                        u.isactive,
                        u.date_created,
                        u.make,
                        u.model,
                        u.os,
                        u.operator_name
                    })
                    .ToListAsync();

                return Json(users);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { Message = "Error fetching users: " + ex.Message });
            }
        }

        #region ManageUsers
        public ActionResult ManageUsers()
        {
            if (!IsAngularRequest())
            {
                return RedirectToAction("Index", "Home");
            }
            if (!cf.SessionCheck("1"))
            {
                return RedirectToAction("Dashboard", "Admin");
            }
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetUsers(string token, string UserName, string Email, string Mobile)
        {
            var message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message = cf.MatchToken(token);
                message.Status = 1;

                if (message.Status == 1)
                {
                    var query = db.tbl_user.AsNoTracking().AsQueryable();

                    if (!string.IsNullOrWhiteSpace(UserName))
                        query = query.Where(a => EF.Functions.Like(a.name, $"%{UserName}%"));

                    if (!string.IsNullOrWhiteSpace(Email))
                        query = query.Where(a => EF.Functions.Like(a.email, $"%{Email}%"));

                    if (!string.IsNullOrWhiteSpace(Mobile))
                        query = query.Where(a => EF.Functions.Like(a.mobile, $"%{Mobile}%"));

                    var result = await query
                        .OrderBy(a => a.name)
                        .Select(u => new
                        {
                            ob_user = new tbl_user
                            {
                                id = u.id,
                                uid = u.uid,
                                token = u.token,
                                name = u.name,
                                password = !string.IsNullOrEmpty(u.password) ? new string('*', 15) : null,
                                email = u.email,
                                make = u.make,
                                model = u.model,
                                os = u.os,
                                operator_name = u.operator_name,
                                company_id = u.company_id,
                                mobile = u.mobile,
                                isactive = u.isactive,
                                m_user_type_id = u.m_user_type_id,
                                last_login = u.last_login,
                                date_created = u.date_created,
                                device_id = u.device_id,
                                gcm_id = u.gcm_id
                            }
                        })
                        .ToListAsync();

                    message.Data = result;
                }
            }
            catch (Exception ex)
            {
                Writelog writelog = new Writelog(db);
                writelog.write_exception_log(0, "AdminHomeController", "GetUsers", DateTime.Now, ex);
            }
            return Json(message);
        }
        #endregion

        #region Manage User
        public ActionResult ManageUser()
        {
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetUserById(string token, int UserID)
        {
            var message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message.Status = 1; // cf.MatchToken(token);
                if (message.Status == 1)
                {
                    var user = await db.tbl_user.AsNoTracking()
                        .Where(a => a.isactive == 1 && a.id == UserID)
                        .Select(u => new tbl_user
                        {
                            id = u.id,
                            uid = u.uid,
                            token = u.token,
                            name = u.name,
                            password = !string.IsNullOrEmpty(u.password) ? new string('*', 15) : null,
                            email = u.email,
                            make = u.make,
                            model = u.model,
                            os = u.os,
                            operator_name = u.operator_name,
                            company_id = u.company_id,
                            mobile = u.mobile,
                            isactive = u.isactive,
                            m_user_type_id = u.m_user_type_id,
                            last_login = u.last_login,
                            date_created = u.date_created,
                            device_id = u.device_id,
                            gcm_id = u.gcm_id
                        })
                        .FirstOrDefaultAsync();

                    message.Data = user;
                }
            }
            catch (Exception ex)
            {
                Writelog writelog = new Writelog(db);
                writelog.write_exception_log(0, "AdminHomeController", "GetUserById", DateTime.Now, ex);
            }
            return Json(message);
        }

        public static string DecodeFrom64(string encodedData)
        {
            System.Text.UTF8Encoding encoder = new System.Text.UTF8Encoding();
            System.Text.Decoder utf8Decode = encoder.GetDecoder();
            byte[] todecode_byte = Convert.FromBase64String(encodedData);
            int charCount = utf8Decode.GetCharCount(todecode_byte, 0, todecode_byte.Length);
            char[] decoded_char = new char[charCount];
            utf8Decode.GetChars(todecode_byte, 0, todecode_byte.Length, decoded_char, 0);
            string result = new String(decoded_char);
            return result;
        }

        public static string EncodePasswordToBase64(string password)
        {
            try
            {
                byte[] encData_byte = System.Text.Encoding.UTF8.GetBytes(password);
                string encodedData = Convert.ToBase64String(encData_byte);
                return encodedData;
            }
            catch (Exception ex)
            {
                throw new Exception("Error in base64Encode" + ex.Message);
            }
        }

        [HttpPost]
        public async Task<JsonResult> SaveUserDetails([FromForm] IFormCollection values, tbl_user users, string token1, string ip)
        {
            var message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message.Status = 1; // cf.MatchToken(token1);

                if (message.Status == 1)
                {
                    users.name = HttpUtility.HtmlEncode(users.name);
                    users.email = HttpUtility.HtmlEncode(users.email);
                    users.mobile = HttpUtility.HtmlEncode(users.mobile);

                    if (users.id == 0)
                    {
                        var exists = await db.tbl_user.AsNoTracking().AnyAsync(a => a.email == users.email && a.isactive == 1);
                        if (!exists)
                        {
                            users.date_created = DateTime.Now;
                            users.isactive = 1;
                            db.tbl_user.Add(users);
                            await db.SaveChangesAsync();
                            message.Status = 1;
                            message.Message = DisplayMessage.UserDetailsSaved;
                        }
                        else
                        {
                            message.Message = DisplayMessage.UserExist;
                        }
                    }
                    else
                    {
                        var getUser = await db.tbl_user.FirstOrDefaultAsync(a => a.id == users.id);
                        if (getUser != null)
                        {
                            getUser.name = users.name;
                            getUser.email = users.email;
                            getUser.mobile = users.mobile;
                            getUser.m_user_type_id = users.m_user_type_id;
                            db.Entry(getUser).State = EntityState.Modified;
                            await db.SaveChangesAsync();
                            message.Status = 2;
                            message.Message = DisplayMessage.UserDetailsUpdated;
                        }
                    }
                    message.token = ""; // cf.CreateToken(ip);
                }
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }

        [HttpPost]
        public async Task<JsonResult> GetUser(int UserID, string token)
        {
            var message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message = cf.MatchToken(token);
                if (message.Status == 1)
                {
                    var user = await db.tbl_user.AsNoTracking()
                        .Where(a => a.id == UserID)
                        .Select(u => new tbl_user
                        {
                            id = u.id,
                            uid = u.uid,
                            token = u.token,
                            name = u.name,
                            password = "", // never return actual password
                            email = u.email,
                            make = u.make,
                            model = u.model,
                            os = u.os,
                            operator_name = u.operator_name,
                            company_id = u.company_id,
                            mobile = u.mobile,
                            isactive = u.isactive,
                            m_user_type_id = u.m_user_type_id,
                            last_login = u.last_login,
                            date_created = u.date_created,
                            device_id = u.device_id,
                            gcm_id = u.gcm_id
                        })
                        .FirstOrDefaultAsync();

                    message.Data = user;
                }
            }
            catch (Exception ex)
            {
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }

        [HttpPost]
        public async Task<JsonResult> DeleteUser(int id, string ip)
        {
            var message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message.Status = 1; // cf.MatchToken(token);
                if (message.Status == 1)
                {
                    var getUser = await db.tbl_user.FirstOrDefaultAsync(a => a.id == id);
                    if (getUser != null)
                    {
                        getUser.isactive = 2;
                        db.Entry(getUser).State = EntityState.Modified;
                        await db.SaveChangesAsync();
                        message.Status = 1;
                        message.Message = DisplayMessage.UserDeleted;
                        if (message.Status == 1)
                            message.token = cf.CreateToken(ip);
                    }
                }
            }
            catch (Exception ex)
            {
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }

        [HttpPost]
        public async Task<JsonResult> UserResetPassword(int userid, string newpwd, string captcha)
        {
            ReturnMessage ret = new ReturnMessage();
            try
            {
                var getUser = await db.tbl_user.FirstOrDefaultAsync(a => a.id == userid);
                if (getUser != null)
                {
                    getUser.password = newpwd;
                    db.Entry(getUser).State = EntityState.Modified;
                    await db.SaveChangesAsync();
                    ret.Status = 1;
                    ret.Message = "Password has been reset successfully.";
                }
                else
                {
                    ret.Status = 0;
                    ret.Message = "Invalid Request";
                }
            }
            catch (Exception ex)
            {
                ret.Status = 0;
                ret.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(ret);
        }

        [HttpPost]
        public async Task<JsonResult> ChangePassword(int userid, string oldpwd, string newpwd, string captcha)
        {
            ReturnMessage ret = new ReturnMessage();
            try
            {
                if (HttpContext?.Session.GetString("CaptchaImageText") == captcha)
                {
                    var getUser = await db.tbl_user.FirstOrDefaultAsync(a => a.id == userid && a.password == oldpwd);
                    if (getUser != null)
                    {
                        getUser.password = newpwd;
                        db.Entry(getUser).State = EntityState.Modified;
                        await db.SaveChangesAsync();
                        ret.Status = 1;
                    }
                    else
                    {
                        ret.Status = 0;
                        ret.Message = "Old password is wrong";
                    }
                }
                else
                {
                    ret.Status = 0;
                    ret.Message = "Invalid CAPTCHA Code !";
                }
            }
            catch (Exception ex)
            {
                ret.Status = 0;
                ret.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(ret);
        }
        #endregion

        #region Manage Sessions
        public ActionResult ManageSession()
        {
            if (!IsAngularRequest() || !cf.SessionCheck())
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetAllNetworkLogs()
        {
            try
            {
                var allLogs = await db.tbl_network_log.AsNoTracking()
                    .Where(log => log.lat != null && log.lon != null)
                    .Select(log => new
                    {
                        log.session_id,
                        log.lat,
                        log.lon,
                        log.rsrp,
                        log.rsrq,
                        log.sinr,
                        log.network,
                        log.timestamp
                    })
                    .ToListAsync();

                return Json(allLogs);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { Message = "An error occurred on the server: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetOperatorCoverageRanking(double min = -95, double max = 0)
        {
            try
            {
                var result = await db.tbl_network_log.AsNoTracking()
                    .Where(l => l.rsrp.HasValue && l.m_alpha_long != null && l.rsrp.Value >= min && l.rsrp.Value <= max)
                    .GroupBy(l => l.m_alpha_long)
                    .Select(g => new { name = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToListAsync();

                return Json(result);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { Message = "Error: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetOperatorQualityRanking(double min = -10, double max = 0)
        {
            try
            {
                var result = await db.tbl_network_log.AsNoTracking()
                    .Where(l => l.rsrq.HasValue && l.m_alpha_long != null && l.rsrq.Value >= min && l.rsrq.Value <= max)
                    .GroupBy(l => l.m_alpha_long)
                    .Select(g => new { name = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToListAsync();

                return Json(result);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { Message = "Error: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetSessions()
        {
            try
            {
                var sessions = await (
                    from s in db.tbl_session.AsNoTracking()
                    join u in db.tbl_user.AsNoTracking() on s.user_id equals u.id
                    orderby s.start_time descending
                    select new
                    {
                        id = s.id,
                        session_name = "Session " + s.id,
                        start_time = s.start_time,
                        end_time = s.end_time,
                        notes = s.notes,

                        start_lat = s.start_lat,
                        start_lon = s.start_lon,
                        end_lat = (double?)s.end_lat,
                        end_lon = (double?)s.end_lon,
                        capture_frequency = (double?)s.capture_frequency,

                        CreatedBy = u.name,
                        mobile = u.mobile,
                        make = u.make,
                        model = u.model,
                        os = u.os,
                        operator_name = u.operator_name,
                        distance_km = s.distance,
                        start_address = s.start_address,
                        end_address = s.end_address
                    })
                    .ToListAsync();

                return Json(sessions);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { Message = "An error occurred on the server: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetSessionsByDateRange(string startDateIso, string endDateIso)
        {
            try
            {
                if (!DateTime.TryParse(startDateIso, out DateTime startDate) ||
                    !DateTime.TryParse(endDateIso, out DateTime endDate))
                {
                    return Json(new { success = false, Message = "Invalid date format" });
                }

                endDate = endDate.Date.AddDays(1).AddTicks(-1);

                var sessionsData = await (
                    from s in db.tbl_session.AsNoTracking()
                    join u in db.tbl_user.AsNoTracking() on s.user_id equals u.id
                    where s.start_time.HasValue && s.start_time.Value >= startDate && s.start_time.Value <= endDate
                    select new
                    {
                        id = s.id,
                        session_name = "Session " + s.id,
                        start_time = s.start_time,
                        end_time = s.end_time,
                        notes = s.notes,
                        start_lat = (double?)s.start_lat,
                        start_lon = s.start_lon,
                        end_lat = s.end_lat,
                        end_lon = s.end_lon,
                        capture_frequency = s.capture_frequency,
                        distance_km = s.distance,
                        start_address = s.start_address,
                        end_address = s.end_address,

                        CreatedBy = u.name,
                        mobile = u.mobile,
                        make = u.make,
                        model = u.model,
                        os = u.os,
                        operator_name = u.operator_name
                    })
                    .ToListAsync();

                var sessionIds = sessionsData.Select(s => s.id).ToList();

                var allLogsForSessions = await db.tbl_network_log.AsNoTracking()
                    .Where(log => sessionIds.Contains(log.session_id))
                    .Select(l => new
                    {
                        l.session_id,
                        l.lat,
                        l.lon,
                        l.rsrp,
                        l.rsrq,
                        l.sinr,
                        l.ul_tpt,
                        l.dl_tpt,
                        l.band,
                        l.network,
                        l.m_alpha_long,
                        l.timestamp
                    })
                    .ToListAsync();

                var logsLookup = allLogsForSessions.ToLookup(log => log.session_id);

                var finalResult = sessionsData.Select(s => new
                {
                    s.id,
                    s.session_name,
                    s.start_time,
                    s.end_time,
                    s.notes,
                    s.start_lat,
                    s.start_lon,
                    s.end_lat,
                    s.end_lon,
                    s.capture_frequency,
                    s.distance_km,
                    s.start_address,
                    s.end_address,
                    s.CreatedBy,
                    s.mobile,
                    s.make,
                    s.model,
                    s.os,
                    s.operator_name,
                    Logs = logsLookup[s.id].Select(l => new
                    {
                        l.lat,
                        l.lon,
                        l.rsrp,
                        l.rsrq,
                        l.sinr,
                        l.ul_tpt,
                        l.dl_tpt,
                        l.band,
                        l.network,
                        l.m_alpha_long,
                        l.timestamp
                    }).ToList()
                }).ToList();

                return Json(finalResult);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { Message = "Error fetching sessions: " + ex.Message });
            }
        }

        [HttpDelete("DeleteSession")]
        public async Task<IActionResult> DeleteSession([FromQuery] string id)
        {
            try
            {
                if (!int.TryParse(id, out int sessionId))
                    return BadRequest("Invalid session id");

                var session = await db.tbl_session.FindAsync(sessionId);

                if (session == null)
                {
                    return NotFound(new { success = false, message = "Session not found." });
                }

                var logs = await db.tbl_network_log
                    .Where(l => l.session_id == sessionId)
                    .ToListAsync();

                if (logs.Any())
                {
                    db.tbl_network_log.RemoveRange(logs);
                }

                db.tbl_session.Remove(session);
                await db.SaveChangesAsync();

                return Ok(new { success = true, message = "Session deleted successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred: " + ex.Message
                });
            }
        }
        #endregion
    }
}