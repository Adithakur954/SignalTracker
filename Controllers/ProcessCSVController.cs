using ExcelDataReader;
using SignalTracker.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Metrics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using CsvHelper;
using static System.Net.WebRequestMethods;
using CsvHelper.Configuration;
using NuGet.Packaging.Signing;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Microsoft.CodeAnalysis.Differencing;
using System.IO.Compression;
using NuGet.Packaging;
using System.Text.Json;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore.Storage;

namespace SignalTracker.Controllers
{
    public class ProcessCSVController : Controller
    {
        private static TimeZoneInfo INDIAN_ZONE = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        ApplicationDbContext db = null;
        CommonFunction cf = null;
        public ProcessCSVController(ApplicationDbContext context, CommonFunction _cf)
        {
            db = context;
            cf = _cf;
        }
        public bool Process(int ExcelId, string directoryPath, string originalFileName, string polygonFilePath, int fileType, int projectId, string Remarks, out string errorMsag)
        {
            bool ret = ProcessFile(fileType, ExcelId, directoryPath, originalFileName, polygonFilePath, projectId, Remarks, out errorMsag);
            return ret;

        }
        public bool ProcessFile(int fileType, int excelID, string directorypath, string originalFileName, string polygonFilePath, int projectId, string Remarks, out string errorMsag)
        {
            bool IsValidSheet = true;
            List<string> errorList = new List<string>();
            List<string> allErrorList = new List<string>();
            List<string> uploadedSuccessSheetList = new List<string>();
            int rowInserted = 0;
            int rowUpdated = 0;
            errorMsag = "";
            try
            {
                if (System.IO.File.Exists(directorypath))
                {
                    var extractpath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedExcels", "Extract" + DateTime.Now.ToString("MMddyyyyHmmss"));

                    List<string> files = new List<string>();
                    List<string> polygonFiles = new List<string>();
                    List<string> imageList = new List<string>();

                    bool isZipFile = IsValidZip(directorypath);

                    if (isZipFile)
                    {
                        (files, imageList) = ExtractZipAndSeparateFiles(directorypath, extractpath);
                    }
                    else
                        files.Add(directorypath);


                    int sessionId = 0;
                    if (fileType == 1)
                    {
                        var session = new tbl_session();
                        session.user_id = 1; //user who is uploading
                        session.type = "network";
                        session.notes = string.IsNullOrEmpty(Remarks) ? "file upload" : Remarks;
                        session.uploaded_on = DateTime.Now;
                        session.tbl_upload_id = 1;

                        db.tbl_session.Add(session);
                        db.SaveChanges();
                        sessionId = session.id; //skg
                    }
                    else if (fileType == 2)
                    {
                        bool isPolygonZipFile = IsValidZip(polygonFilePath);
                        if (isPolygonZipFile)
                        {
                            polygonFiles = ExtractJsonFiles(polygonFilePath, extractpath);
                        }
                        else
                            polygonFiles.Add(polygonFilePath);

                        foreach (string file in polygonFiles)
                        {
                            if (!string.IsNullOrEmpty(file))
                            {
                                bool polygonOk = ProcessPredictionPloygonJson(file, excelID, projectId, ref rowInserted, ref rowUpdated, out errorList);
                                if (!polygonOk)
                                {
                                    IsValidSheet = false;
                                }
                                if (errorList.Count > 0)
                                    allErrorList.AddRange(errorList);
                            }
                        }
                    }



                    foreach (string file in files)
                    {

                        if (fileType == 1)
                            IsValidSheet = ProcessNetLogWorkSheet(sessionId, file, imageList, excelID, ref rowInserted, ref rowUpdated, out errorList);
                        else if (fileType == 2)
                            IsValidSheet = ProcessCtrPredictionSheet(file, excelID, projectId, ref rowInserted, ref rowUpdated, out errorList);

                        if (errorList.Count > 0)
                            allErrorList.AddRange(errorList);
                    }
                    //Move images in another folder
                    if (IsValidSheet && imageList.Count > 0)
                    {
                        var imgpath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedExcels", "Images_" + sessionId);
                        if (!Directory.Exists(imgpath))
                            Directory.CreateDirectory(imgpath);
                        foreach (var imagePath in imageList)
                        {
                            string fileName = Path.GetFileName(imagePath);
                            string destPath = Path.Combine(imgpath, fileName);

                            if (System.IO.File.Exists(destPath))
                                System.IO.File.Delete(destPath);

                            System.IO.File.Move(imagePath, destPath);
                        }
                    }
                    try
                    {
                        Directory.Delete(extractpath, recursive: true);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                IsValidSheet = false;
                errorMsag = "Exception " + ex.Message;
            }
            //if (errorList.Count > 0)
            //{
            //    errorMsag += string.Join(Environment.NewLine, errorList);
            //}
            if (allErrorList.Count > 0)
            {
                errorMsag = "Errorneous Sheets:" + Environment.NewLine;
                errorMsag += string.Join(Environment.NewLine, allErrorList);

                if (uploadedSuccessSheetList.Count > 0)
                {
                    errorMsag += Environment.NewLine;
                    errorMsag += "Uploaded Sheets: " + Environment.NewLine;
                    errorMsag += string.Join(Environment.NewLine, uploadedSuccessSheetList);
                }
            }
            else if (fileType == 15 && uploadedSuccessSheetList.Count > 0)
            {
                errorMsag += Environment.NewLine;
                errorMsag += "Uploaded Sheets: " + Environment.NewLine;
                errorMsag += string.Join(Environment.NewLine, uploadedSuccessSheetList);
            }

            return IsValidSheet;
        }
        public bool IsValidJson(string filePath)
        {
            try
            {
                string jsonContent = System.IO.File.ReadAllText(filePath);
                using (JsonDocument.Parse(jsonContent))
                {
                    return true; // Valid JSON
                }
            }
            catch
            {
                return false; // Invalid JSON
            }
        }
        public bool IsValidZip(string filePath)
        {
            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read, true))
                {
                    return archive.Entries.Count > 0; // ZIP is valid and has at least one entry
                }
            }
            catch
            {
                return false; // Not a valid ZIP file
            }
        }
        public (List<string> CsvFiles, List<string> ImageFiles) ExtractZipAndSeparateFiles(string zipFilePath, string extractPath)
        {
            List<string> csvFiles = new List<string>();
            List<string> imageFiles = new List<string>();

            if (!Directory.Exists(extractPath))
                Directory.CreateDirectory(extractPath);

            ZipFile.ExtractToDirectory(zipFilePath, extractPath, overwriteFiles: true);

            string[] imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

            var allFiles = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories);

            foreach (var file in allFiles)
            {
                string ext = Path.GetExtension(file).ToLower();
                if (ext == ".csv")
                {
                    csvFiles.Add(file);
                }
                else if (imageExtensions.Contains(ext))
                {
                    imageFiles.Add(file);
                }
            }

            return (csvFiles, imageFiles);
        }
        public List<string> ExtractJsonFiles(string zipFilePath, string extractPath)
        {
            List<string> ltsjsonFiles = new List<string>();

            if (!Directory.Exists(extractPath))
                Directory.CreateDirectory(extractPath);

            ZipFile.ExtractToDirectory(zipFilePath, extractPath, overwriteFiles: true);

            var jsonFiles = Directory.GetFiles(extractPath, "*.json", SearchOption.AllDirectories);
            var geoJsonFiles = Directory.GetFiles(extractPath, "*.geojson", SearchOption.AllDirectories);

            ltsjsonFiles.AddRange(jsonFiles);
            ltsjsonFiles.AddRange(geoJsonFiles);

            return ltsjsonFiles;
        }

        #region Calculate Sheet-> Network Log sheet
        public bool ProcessNetLogWorkSheet(int sessionId, string filePath, List<string> imageList, int ExcelID, ref int rowInserted, ref int rowUpdated, out List<string> errorList)
        {
            bool isColValValid = true;
            int userId = 0;
            errorList = new List<string>();

            //var excel_details = db.tbl_upload_history.FirstOrDefault(a => a.id == ExcelID);
            //if (excel_details != null)
            //    userId = excel_details.uploaded_by;
            string fileName = System.IO.Path.GetFileName(filePath);

            bool isValidTemplate = true;
            try
            {
                using var reader = new StreamReader(filePath, Encoding.UTF8);
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    PrepareHeaderForMatch = args => args.Header.Trim(),
                    MissingFieldFound = null,
                };

                using var csv = new CsvReader(reader, config);
                string[] expectedHeaders = "Timestamp,Latitude,Longitude,Battery Level,Network Type,Download Speed (KB/s),Upload Speed (KB/s),Total Rx (KB),Total Tx (KB), HotSpot, Running Apps, MOS, Jitter, Latency, Packet Loss,Call State ,CI, PCI, RSRP, RSRQ, SINR, DL THPT, UL THPT, EARFCN, VOLTE CALL, BAND, CQI, BLER, Alpha Long, Alpha Short, No of Cells,CellInfo_1,CellInfo_2".Split(',').Select(a => a.Trim()).ToArray();

                string missingHeaders = "";
                isValidTemplate = ValidateCsvHeaders(filePath, expectedHeaders, out missingHeaders);

                List<NetworkLogModel> records = new List<NetworkLogModel>();
                if (isValidTemplate)
                {
                    records = csv.GetRecords<NetworkLogModel>().ToList();
                }
                else
                {
                    errorList.Add(fileName + " invalid file:- '" + missingHeaders + "' columns are missing");
                    return false;
                }

                //DateTime parsedDate= DateTime.Now;
                //var dtSplit = originalFileName.Split('_');
                //if (dtSplit.Length>= 2)
                //{
                //    bool success = DateTime.TryParseExact(dtSplit[1], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,  System.Globalization.DateTimeStyles.None, out parsedDate);
                //    if (!success)
                //        parsedDate = DateTime.Now;
                //}



                if (isValidTemplate && records.Count > 0)
                {
                    using (var dbContextTransaction = db.Database.BeginTransaction())
                    {
                        try
                        {

                            int rowIndex = 0;
                            foreach (var row in records)
                            {
                                rowIndex++;
                                string? Timestamp = GetColStringVal(row.Timestamp, out isColValValid);
                                if (!isColValValid)
                                {
                                    errorList.Add($"Row {rowIndex} ({Timestamp}): Invalid Timestamp in sheet " + fileName);
                                    break;
                                }
                                if (isColValValid)
                                {

                                    string rawDate = row.Timestamp.Trim().Trim('\uFEFF');

                                    DateTime timestamp = DateTime.ParseExact(rawDate, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                                    var networkLog = db.tbl_network_log.FirstOrDefault(x => x.session_id == sessionId && x.timestamp == timestamp) ?? new tbl_network_log();

                                    networkLog.session_id = sessionId;
                                    networkLog.timestamp = timestamp;
                                    networkLog.lat = ParseFloat(row.Latitude);
                                    networkLog.lon = ParseFloat(row.Longitude);
                                    networkLog.battery = ParseInt(row.Battery);

                                    networkLog.dls = row.dls;
                                    networkLog.uls = row.uls;
                                    networkLog.call_state = row.call_state;

                                    if (!string.IsNullOrEmpty(row.HotSpot))
                                    {
                                        var file = imageList.Where(a => a == row.HotSpot).FirstOrDefault();
                                        if (file != null)
                                        {
                                            if (System.IO.File.Exists(file))
                                                networkLog.image_path = row.HotSpot;
                                        }
                                        else
                                            networkLog.hotspot = row.HotSpot;
                                    }

                                    networkLog.apps = row.Apps;
                                    networkLog.num_cells = ParseInt(row.num_cells);

                                    networkLog.network = row.Network;
                                    networkLog.m_alpha_long = row.m_alpha_long;
                                    networkLog.m_alpha_short = row.m_alpha_short;
                                    networkLog.pci = row.PCI;
                                    networkLog.earfcn = row.EARFCN;
                                    bool isValidFloat = false;
                                    networkLog.rsrp = ValidParseFloat(row.RSRP, out isValidFloat);
                                    if (!isValidFloat)
                                    {
                                        errorList.Add($"Row {rowIndex} ({row.RSRP}): Invalid value of RSRP in sheet " + fileName);
                                        continue;
                                    }

                                    networkLog.rsrq = ValidParseFloat(row.RSRQ, out isValidFloat);
                                    if (!isValidFloat)
                                    {
                                        errorList.Add($"Row {rowIndex} ({row.RSRQ}): Invalid value of RSRQ in sheet " + fileName);
                                        continue;
                                    }
                                    networkLog.sinr = ValidParseFloat(row.SINR, out isValidFloat);
                                    if (!isValidFloat)
                                    {
                                        errorList.Add($"Row {rowIndex} ({row.SINR}): Invalid value of SINR in sheet " + fileName);
                                        continue;
                                    }

                                    networkLog.total_rx_kb = row.total_rx_kb;
                                    networkLog.total_tx_kb = row.total_tx_kb;
                                    networkLog.mos = ValidParseFloat(row.MOS, out isValidFloat);
                                    if (!isValidFloat)
                                    {
                                        errorList.Add($"Row {rowIndex} ({row.MOS}): Invalid value of MOS in sheet " + fileName);
                                        continue;
                                    }
                                    networkLog.jitter = ValidParseFloat(row.Jitter, out isValidFloat);
                                    if (!isValidFloat)
                                    {
                                        errorList.Add($"Row {rowIndex} ({row.Jitter}): Invalid value of Jitter in sheet " + fileName);
                                        continue;
                                    }
                                    networkLog.latency = ValidParseFloat(row.Latency, out isValidFloat);
                                    if (!isValidFloat)
                                    {
                                        errorList.Add($"Row {rowIndex} ({row.Latency}): Invalid value of Latency in sheet " + fileName);
                                        continue;
                                    }
                                    networkLog.packet_loss = ValidParseFloat(row.packet_loss, out isValidFloat);
                                    if (!isValidFloat)
                                    {
                                        errorList.Add($"Row {rowIndex} ({row.packet_loss}): Invalid value of packet_loss in sheet " + fileName);
                                        continue;
                                    }

                                    networkLog.dl_tpt = row.dl_tpt;
                                    networkLog.ul_tpt = row.ul_tpt;
                                    networkLog.volte_call = row.volte_call;
                                    networkLog.band = row.BAND;
                                    networkLog.cqi = ValidParseFloat(row.CQI, out isValidFloat);
                                    if (!isValidFloat)
                                    {
                                        errorList.Add($"Row {rowIndex} ({row.CQI}): Invalid value of CQI in sheet " + fileName);
                                        continue;
                                    }
                                    networkLog.bler = row.BLER;

                                    networkLog.primary_cell_info_1 = row.primary_cell_info_1;
                                    networkLog.primary_cell_info_2 = row.primary_cell_info_2;



                                    if (networkLog.id > 0)
                                        db.Entry(networkLog).State = EntityState.Modified;
                                    else
                                        db.tbl_network_log.Add(networkLog);

                                }
                                else
                                    break;

                            }

                            if (isColValValid)
                            {
                                db.SaveChanges();
                                dbContextTransaction.Commit();
                            }
                            else
                            {
                                dbContextTransaction.Rollback();
                                var changedEntries = db.ChangeTracker.Entries()
                                    .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
                                    .ToList();

                                foreach (var entry in changedEntries)
                                    entry.State = EntityState.Detached;
                            }
                        }
                        catch (Exception ex)
                        {
                            dbContextTransaction.Rollback();
                            var changedEntries = db.ChangeTracker.Entries()
                                    .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
                                    .ToList();

                            foreach (var entry in changedEntries)
                                entry.State = EntityState.Detached;

                            errorList.Add($"{fileName} General error: {(ex.InnerException != null ? ex.InnerException.Message : ex.Message)}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errorList.Add($"{fileName} General error: {(ex.InnerException != null ? ex.InnerException.Message : ex.Message)}");
                return false;
            }

            return isColValValid;
        }
        #endregion
        #region Calculate Sheet-> Prediction sheet
        public bool ProcessCtrPredictionSheet(string filePath, int ExcelID, int projectId, ref int rowInserted, ref int rowUpdated, out List<string> errorList)
        {
            bool isColValValid = true;
            int userId = 0;
            errorList = new List<string>();

            //var excel_details = db.tbl_upload_history.FirstOrDefault(a => a.id == ExcelID);
            //if (excel_details != null)
            //    userId = excel_details.uploaded_by;

            bool isValidTemplate = true;

            using var reader = new StreamReader(filePath, Encoding.UTF8);
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                PrepareHeaderForMatch = args => args.Header.Trim(),
            };

            using var csv = new CsvReader(reader, config);
            string[] expectedHeaders = "latitude,longitude,RSRP,RSRQ,SINR,ServingCell,azimuth,tx_power,height,band,earfcn,reference_signal_power,PCI,Mtilt,Etilt".Split(',').Select(a => a.Trim()).ToArray();

            string missingHeaders = "";
            isValidTemplate = ValidateCsvHeaders(filePath, expectedHeaders, out missingHeaders);

            List<PredictionDtatModel> records = new List<PredictionDtatModel>();
            if (isValidTemplate)
            {
                records = csv.GetRecords<PredictionDtatModel>().ToList();
            }
            else
            {
                errorList.Add("invalid file:- '" + missingHeaders + "' columns are missing");
                return false;
            }
            if (isValidTemplate && records.Count > 0)
            {
                using (var dbContextTransaction = db.Database.BeginTransaction())
                {
                    try
                    {
                        int sessionId = 0;
                        int rowIndex = 1;
                        foreach (var row in records)
                        {
                            //string? Email = GetColStringVal(row.Timestamp, out isColValValid);
                            //if (!isColValValid)
                            //{
                            //    errorList.Add($"Row {rowIndex} ({Email}): Invalid Timestamp in sheet");
                            //    break;
                            //}
                            if (isColValValid)
                            {



                                var obj = new tbl_prediction_data();
                                obj.tbl_project_id = projectId;
                                obj.lat = ParseFloat(row.latitude);
                                obj.lon = ParseFloat(row.longitude);
                                obj.rsrp = ParseFloat(row.RSRP);
                                obj.rsrq = ParseFloat(row.RSRQ);
                                obj.sinr = ParseFloat(row.SINR);
                                obj.serving_cell = row.ServingCell;
                                obj.azimuth = row.azimuth;
                                obj.tx_power = row.tx_power;
                                obj.height = row.height;
                                obj.band = row.band;
                                obj.earfcn = row.earfcn;
                                obj.reference_signal_power = row.reference_signal_power;
                                obj.pci = row.PCI;
                                obj.mtilt = row.Mtilt;
                                obj.etilt = row.Etilt;

                                if (obj.id > 0)
                                    db.Entry(obj).State = EntityState.Modified;
                                else
                                    db.tbl_prediction_data.Add(obj);

                            }
                            else
                                break;
                            rowIndex++;
                        }

                        if (isColValValid)
                        {
                            db.SaveChanges();
                            dbContextTransaction.Commit();
                        }
                        else
                        {
                            dbContextTransaction.Rollback();
                            var changedEntries = db.ChangeTracker.Entries()
                                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
                                .ToList();

                            foreach (var entry in changedEntries)
                                entry.State = EntityState.Detached;
                        }
                    }
                    catch (Exception ex)
                    {
                        dbContextTransaction.Rollback();
                        var changedEntries = db.ChangeTracker.Entries()
                                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
                                .ToList();

                        foreach (var entry in changedEntries)
                            entry.State = EntityState.Detached;

                        errorList.Add($"General error: {(ex.InnerException != null ? ex.InnerException.Message : ex.Message)}");
                        return false;
                    }
                }
            }

            return isColValValid;
        }
        public bool ProcessPredictionPloygonJson(string filePath, int ExcelID, int projectId, ref int rowInserted, ref int rowUpdated, out List<string> errorList)
        {
            bool isColValValid = true;
            errorList = new List<string>();

            bool isFileFile = IsValidJson(filePath);

            List<PredictionDtatModel> records = new List<PredictionDtatModel>();
            if (isFileFile)
            {
                string json = System.IO.File.ReadAllText(filePath);
                var geoJson = JsonConvert.DeserializeObject<GeoJson>(json);
                if (geoJson != null)
                {
                    using (var dbContextTransaction = db.Database.BeginTransaction())
                    {
                        try
                        {
                            foreach (var feature in geoJson.Features.Where(f => f.Geometry?.Type == "Polygon"))
                            {
                                var coords = feature.Geometry.Coordinates[0]; // Outer ring
                                var first = coords[0];
                                var last = coords[^1];

                                // Ensure the polygon is closed
                                if (first[0] != last[0] || first[1] != last[1])
                                {
                                    coords.Add(first);
                                }

                                var coordsText = string.Join(", ", coords.Select(c => $"{c[0]} {c[1]}")); // lng lat
                                var polygonWKT = $"POLYGON(({coordsText}))";
                                var name = feature.Properties.TryGetValue("name", out var val) ? val?.ToString() ?? "Unnamed" : "Unnamed";

                                string sql = @"
                                        INSERT INTO map_regions (tbl_project_id, name, region, status)
                                        VALUES ({0}, {1}, ST_GeomFromText({2}, 4326), 1)";

                                db.Database.ExecuteSqlRaw(sql, projectId, name, polygonWKT);
                            }

                            db.SaveChanges();
                            dbContextTransaction.Commit();

                            /*

                            var regions = geoJson.Features
                            .Where(f => f.Geometry?.Type == "Polygon")
                            .Select(f =>
                            {
                                var coords = f.Geometry.Coordinates[0]; // Outer ring
                                var coordsText = string.Join(", ", coords.Select(c => $"{c[0]} {c[1]}")); // lng lat
                                var name = f.Properties.TryGetValue("name", out var val) ? val?.ToString() ?? "Unnamed" : "Unnamed";

                                return new map_regions
                                {
                                    name = name,
                                    region = $"POLYGON(({coordsText}))"
                                };
                            }).ToList();

                            // Add and Save
                            foreach (var region in regions)
                            {
                                string sql = @"INSERT INTO map_regions (tbl_project_id, name, region, status) VALUES ({0}, {1}, ST_GeomFromText({2}, 4326), 1)";
                                db.Database.ExecuteSqlRaw(sql, projectId, region.name, region.region);
                            }

                            db.SaveChanges();
                            dbContextTransaction.Commit();
                            */
                        }
                        catch (Exception ex)
                        {
                            dbContextTransaction.Rollback();

                            errorList.Add($"General error: {(ex.InnerException != null ? ex.InnerException.Message : ex.Message)}");
                            return false;
                        }
                    }
                }
            }
            else
            {
                errorList.Add("invalid file:- '" + System.IO.Path.GetFileName(filePath));
                return false;
            }

            return isColValValid;
        }
        #endregion
        public bool ValidateCsvHeaders(string filePath, string[] expectedHeaders, out string missingHeaders)
        {
            bool isValidTemplate = false;
            missingHeaders = "";

            var firstLine = System.IO.File.ReadLines(filePath).FirstOrDefault();
            if (firstLine != null)
            {

                var actualHeaders = firstLine.Split(',').Select(h => h.Trim()).ToArray();

                var missing = expectedHeaders.Where(expected => !actualHeaders.Contains(expected, StringComparer.OrdinalIgnoreCase)).ToList();

                isValidTemplate = missing.Count == 0;

                if (!isValidTemplate)
                {
                    missingHeaders = string.Join(", ", missing);
                }
            }

            return isValidTemplate;
        }

        public DataTable ReadCsvManual(string filePath)
        {
            var dt = new DataTable();

            var lines = System.IO.File.ReadAllLines(filePath);
            if (lines.Length == 0) return dt;

            var headers = lines[0].Split(',');
            foreach (var header in headers)
                dt.Columns.Add(header.Trim());

            for (int i = 1; i < lines.Length; i++)
            {
                var values = lines[i].Split(',');
                var row = dt.NewRow();
                for (int j = 0; j < headers.Length && j < values.Length; j++)
                    row[j] = values[j].Trim();
                dt.Rows.Add(row);
            }

            return dt;
        }
        #region Excel Read Methods
        private float? ValidParseFloat(string? value, out bool isValid)
        {
            isValid = false;

            if (string.IsNullOrWhiteSpace(value))
            {
                isValid = true;
                return null;
            }

            if (float.TryParse(value, out float result) && !float.IsNaN(result) && !float.IsInfinity(result))
            {
                isValid = true;
                if (result == 2147483647)
                {
                    isValid = false;
                    return null;
                }
                return result;
            }

            return null;
        }
        private float? ParseFloat(string? value)
        {
            return float.TryParse(value, out var result) ? result : null;
        }

        private int? ParseInt(string? value)
        {
            return int.TryParse(value, out var result) ? result : null;
        }

        private DateTime? ParseDateTime(string? value)
        {
            return DateTime.TryParse(value, out var result) ? result : null;
        }
        public string readDateFromExcel(string val, int ExcelID, string Row_no)
        {
            string correcteddate = val;
            if (val != "" && !val.ToLower().Equals("nil") && !val.ToLower().Equals("na") && !val.ToLower().Equals("-") && !val.ToLower().Contains("for") && !val.ToLower().Contains("the") && !val.ToLower().Contains("yet") && !val.ToLower().Contains("not") && !val.ToLower().Contains("vacancy"))
            {
                try
                {
                    double d = double.Parse(val);
                    DateTime conv = DateTime.FromOADate(d);
                    correcteddate = conv.ToString("dd-MM-yyyy");//.ToShortDateString(); //ToString("dd/MM/yyyy");
                }
                catch (Exception ex)
                {
                    DateTime dateTime;
                    if (DateTime.TryParseExact(val, "dd/MMM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                    {
                        correcteddate = dateTime.ToString("dd-MM-yyyy");
                    }
                    else if (DateTime.TryParseExact(val, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                    {
                        correcteddate = dateTime.ToString("dd-MM-yyyy");
                    }
                    else if (DateTime.TryParseExact(val, "dd MMM yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                    {
                        correcteddate = dateTime.ToString("dd-MM-yyyy");
                    }
                    else if (DateTime.TryParseExact(val, "d MMM yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                    {
                        correcteddate = dateTime.ToString("dd-MM-yyyy");
                    }
                    else if (DateTime.TryParseExact(val, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                    {
                        correcteddate = dateTime.ToString("dd-MM-yyyy");
                    }
                    else if (val.Contains("-") || val.Contains("/"))
                    {
                        string[] arr = { };
                        if (val.Contains("-"))
                            arr = val.Split('-');
                        else if (val.Contains("/"))
                            arr = val.Split('/');

                        if (arr.Length == 3)
                        {
                            int dd = Convert.ToInt32(arr[0]);
                            int mm;
                            if (int.TryParse(arr[1], out mm))
                            {
                                mm = Convert.ToInt32(arr[1]);
                            }
                            else
                            {
                                if (arr[1].Trim().Length == 3)
                                {
                                    string t = arr[1].Trim().ToLower();
                                    mm = getMonth(t);
                                }
                                else if (arr[1].Trim().Length > 3)
                                {
                                    string t = arr[1].Trim().ToLower();
                                    mm = getMonth1(t);
                                }
                            }

                            int yy = Convert.ToInt32(arr[2]);
                            if (arr[2].Length == 2)
                            {
                                arr[2] = "20" + arr[2];
                                yy = Convert.ToInt32(arr[2]);
                            }
                            if (mm > 12)
                            {
                                int t = dd;
                                dd = mm;
                                mm = t;
                            }
                            if (mm < 10)
                            {
                                if (dd < 10)
                                    correcteddate = "0" + dd + "-0" + mm + "-" + yy;
                                else
                                    correcteddate = dd + "-0" + mm + "-" + yy;
                            }
                            else
                            {
                                if (dd < 10)
                                    correcteddate = "0" + dd + "-" + mm + "-" + yy;
                                else
                                    correcteddate = dd + "-" + mm + "-" + yy;
                            }
                        }
                    }
                    else if (val.Contains("."))
                    {
                        string[] arr = val.Split('.');
                        if (arr.Length == 3)
                        {
                            int dd = Convert.ToInt32(arr[0]);
                            int mm;
                            if (int.TryParse(arr[1], out mm))
                            {
                                mm = Convert.ToInt32(arr[1]);
                            }
                            int yy = Convert.ToInt32(arr[2]);

                            if (mm < 10)
                            {
                                if (dd < 10)
                                    correcteddate = "0" + dd + "-0" + mm + "-" + yy;
                                else
                                    correcteddate = dd + "-0" + mm + "-" + yy;
                            }
                            else
                            {
                                if (dd < 10)
                                    correcteddate = "0" + dd + "-" + mm + "-" + yy;
                                else
                                    correcteddate = dd + "-" + mm + "-" + yy;
                            }
                        }
                    }
                    else if (val.Contains("th") || val.Contains("rd"))
                    {
                        //11th May, 2018
                        string[] arr;
                        if (val.Contains("th"))
                            arr = val.Split(new string[] { "th" }, StringSplitOptions.None);
                        else
                            arr = val.Split(new string[] { "rd" }, StringSplitOptions.None);

                        if (arr.Length == 2)
                        {
                            int dd = Convert.ToInt32(arr[0]);
                            string mmyy = arr[1];
                            int mm;
                            int yy;
                            if (mmyy.Contains(","))
                            {
                                string[] arr1 = mmyy.Split(',');
                                if (arr1.Length == 2)
                                {
                                    if (int.TryParse(arr1[0], out mm))
                                    {
                                        mm = Convert.ToInt32(arr1[0]);
                                    }
                                    else
                                    {
                                        mm = getMonth(arr1[0]);
                                    }

                                    yy = Convert.ToInt32(arr1[1]);
                                    if (mm < 10)
                                        correcteddate = dd + "-0" + mm + "-" + yy;
                                    else
                                        correcteddate = dd + "-" + mm + "-" + yy;
                                }
                                else
                                {
                                    //error
                                    int er = 0;
                                }

                            }
                        }
                    }
                    else
                    {
                        string Error = "Invalid date format - The value read is # " + val + " #";
                        //ExcelErrorLog(ExcelID, Table_No, Row_no, Error);
                        correcteddate = val;
                    }
                }
            }
            return correcteddate;
        }
        private int getMonth(string t)
        {
            t = t.ToLower().Trim();
            int mm = 0;
            if (t.Equals("jan"))
                mm = 1;
            else if (t.Equals("feb"))
                mm = 2;
            else if (t.Equals("mar"))
                mm = 3;
            else if (t.Equals("apr"))
                mm = 4;
            else if (t.Equals("may"))
                mm = 5;
            else if (t.Equals("jun"))
                mm = 6;
            else if (t.Equals("jul"))
                mm = 7;
            else if (t.Equals("aug"))
                mm = 8;
            else if (t.Equals("sep"))
                mm = 9;
            else if (t.Equals("oct"))
                mm = 10;
            else if (t.Equals("nov"))
                mm = 11;
            else if (t.Equals("dec"))
                mm = 12;

            return mm;
        }

        private int getMonth1(string t)
        {
            t = t.ToLower().Trim();
            int mm = 0;
            if (t.Equals("january"))
                mm = 1;
            else if (t.Equals("february"))
                mm = 2;
            else if (t.Equals("march"))
                mm = 3;
            else if (t.Equals("april"))
                mm = 4;
            else if (t.Equals("may"))
                mm = 5;
            else if (t.Equals("june"))
                mm = 6;
            else if (t.Equals("july"))
                mm = 7;
            else if (t.Equals("august"))
                mm = 8;
            else if (t.Equals("september"))
                mm = 9;
            else if (t.Equals("october"))
                mm = 10;
            else if (t.Equals("november"))
                mm = 11;
            else if (t.Equals("december"))
                mm = 12;

            return mm;
        }
        private T GetColValOld<T>(object val, out bool isValidVal)
        {
            isValidVal = true;
            if (val.ToString() == "")
            {
                isValidVal = true;
                return default(T);
            }
            if (val == null || val == DBNull.Value || val.ToString() == "")
            {
                isValidVal = false;
                return default(T);

            }
            return (T)Convert.ChangeType(val, typeof(T));
        }
        private string? GetColStringVal(object val, out bool isValidVal)
        {
            isValidVal = true;
            if (val == null || val == DBNull.Value || val.ToString() == "")
            {
                isValidVal = false;
                return null;
            }
            if (val.ToString() == "NA")
            {
                return null;
            }
            return val.ToString();
        }
        private T? GetColVal<T>(object val, out bool isValidVal) where T : struct
        {
            isValidVal = true;

            if (val == null || val == DBNull.Value || val.ToString() == "")
            {
                isValidVal = false;
                return null;  // Return null for nullable value types
            }

            if (val.ToString() == "NA")
            {
                return null;  // Explicitly return null for "NA"
            }

            try
            {
                return (T)Convert.ChangeType(val, typeof(T));
            }
            catch
            {
                isValidVal = false;
                return null;  // Return null if conversion fails
            }
        }
        private string ParseFinancialYear(string fyText)
        {
            string ret = "";
            try
            {
                var parts = fyText.Replace("FY", "").Trim().Split('-');
                if (parts.Length == 2 && int.TryParse(parts[1], out int endYearSuffix))
                {
                    string startYear = parts[0].Trim();
                    if (startYear.Length == 4)
                    {
                        ret = startYear + "-" + endYearSuffix;
                    }
                }
            }
            catch { }
            return ret;
        }
        public string ExtractFinancialYear(string input)
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, @"\b(\d{4}-\d{2})\b");
            return match.Success ? match.Value : string.Empty;
        }
        #endregion
    }
}