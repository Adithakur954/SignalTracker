using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SignalTracker.Helper;
using SignalTracker.Models;

namespace SignalTracker.Controllers
{
    
    [Route("api/[controller]")]
    [ApiController]
    public class SettingController : ControllerBase
    {
        private readonly ApplicationDbContext db;
        private readonly CommonFunction cf;

        public SettingController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            db = context;
            cf = new CommonFunction(context, httpContextAccessor);
        }

        /// <summary>
        /// Check if session is valid (API replacement for SettingIndex view).
        /// </summary>
        // [HttpGet("CheckSession")]
         
        // public IActionResult CheckSession()
        // {
        //     if (!cf.SessionCheck())
        //     {
        //         return Unauthorized(new { Status = 0, Message = "Unauthorized" });
        //     }

        //     return Ok(new { Status = 1, Message = "Session valid" });
        // }

        /// <summary>
        /// Get threshold settings for logged-in user (or default).
        /// </summary>
        [HttpGet("GetThresholdSettings")]
        public IActionResult GetThresholdSettings()
        {
            var message = new ReturnAPIResponse();

            try
            {
                // if (!cf.SessionCheck())
                // {
                //     return Unauthorized(new { Status = 0, Message = "Unauthorized" });
                // }

                var setting = db.thresholds.FirstOrDefault(x => x.user_id == cf.UserId)
                              ?? db.thresholds.FirstOrDefault(x => x.is_default == 1);

                message.Status = 1;
                message.Data = setting;
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = "Error: " + ex.Message;
            }

            return Ok(message);
        }

        /// <summary>
        /// Save or update threshold settings for logged-in user.
        /// </summary>
        [HttpPost("SaveThreshold")]
        public IActionResult SaveThreshold([FromBody] thresholds model)
        {
            var response = new ReturnAPIResponse();

            try
            {
                // if (model == null || !cf.SessionCheck())
                // {
                //     return BadRequest(new { Status = 0, Message = "Invalid Request" });
                // }

                var existing = db.thresholds.FirstOrDefault(x => x.user_id == cf.UserId);

                if (existing != null)
                {
                    // Update existing
                    existing.rsrp_json = model.rsrp_json;
                    existing.rsrq_json = model.rsrq_json;
                    existing.sinr_json = model.sinr_json;
                    existing.dl_thpt_json = model.dl_thpt_json;
                    existing.ul_thpt_json = model.ul_thpt_json;
                    existing.volte_call = model.volte_call;
                    existing.lte_bler_json = model.lte_bler_json;
                    existing.mos_json = model.mos_json;

                    db.Entry(existing).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                }
                else
                {
                    // Insert new
                    model.id = 0;
                    model.is_default = 0;
                    model.user_id = cf.UserId;
                    db.thresholds.Add(model);
                }

                db.SaveChanges();

                response.Status = 1;
                response.Message = "Threshold saved successfully.";
                response.Data = model.id;
            }
            catch (Exception ex)
            {
                response.Status = 0;
                response.Message = "Error: " + ex.Message;
            }

            return Ok(response);
        }
    }
}
