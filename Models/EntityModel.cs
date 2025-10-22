using NuGet.Protocol.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Xml;

namespace SignalTracker.Models
{
    
    public class tbl_user
    {
        public int id { get; set; }
        public string? uid { get; set; }
        public string? token { get; set; }
        public string name { get; set; }       
        public string? password { get; set; }
        public string? email { get; set; }

        public string? make { get; set; }
        public string? model { get; set; }
        public string? os { get; set; }
        public string? operator_name { get; set; }

        public int? company_id { get; set; }
        public string? mobile { get; set; }
        public int isactive { get; set; }
        public int m_user_type_id { get; set; }
        public DateTime? last_login { get; set; } 
        public DateTime? date_created { get; set; }

        public string? device_id { get; set; }

        public string? gcm_id { get; set; }
    }
    public class tbl_user_login_audit_details
    {
        public int id { get; set; }
        public string username { get; set; }
        public string ip_address { get; set; }
        public int login_status { get; set; }
        public DateTime date_of_creation { get; set; }
    }    

    public class m_user_type
    {
        public int id { get; set; }
        public string type { get; set; }
        public int m_status_id { get; set; }
    }
    public class m_email_setting
    {
        public int ID { get; set; }
        public string SMTPServer { get; set; }
        public string SMTPPort { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
        public bool SSLayer { get; set; }
        public string received_email_on { get; set; }
        public int m_Status_ID { get; set; }
        public DateTime Date_of_Creation { get; set; }
    }
    public class exception_history
    {
        public int id { get; set; }
        public int user_id { get; set; }
        public string source_file { get; set; }
        public string page { get; set; }
        public string exception { get; set; }
        public DateTime exception_date { get; set; }
    }
    public class tbl_session
    {
        public int id { get; set; }
        public int user_id { get; set; }
        public DateTime? start_time { get; set; }
        public DateTime? end_time { get; set; }
        public float? start_lat { get; set; }
        public float? start_lon { get; set; }
        public float? end_lat { get; set; }
        public float? end_lon { get; set; }
        public float? distance { get; set; }
        public int? capture_frequency { get; set; }

        public string? type { get; set; }
        public string? notes { get; set; }

        public string? start_address { get; set; }
        public string? end_address { get; set; }

        public DateTime? uploaded_on { get; set; }

        public int? tbl_upload_id { get; set; }
    }

    public class SessionWithUserDTO
    {
        public int id { get; set; }
        public int user_id { get; set; }
        public DateTime? start_time { get; set; }
        public DateTime? end_time { get; set; }
        public float? start_lat { get; set; }
        public float? start_lon { get; set; }
        public float? end_lat { get; set; }
        public float? end_lon { get; set; }
        public float? distance { get; set; }
        public int? capture_frequency { get; set; }

        public string? type { get; set; }
        public string? notes { get; set; }

        public string? start_address { get; set; }
        public string? end_address { get; set; }

        public DateTime? uploaded_on { get; set; }

        public string? tbl_upload_id { get; set; }

        public string? name { get; set; }
        public string? mobile { get; set; }
        public string? make { get; set; }
        public string? model { get; set; }
        public string? os { get; set; }
        public string? operator_name { get; set; }
        // ... include other relevant fields
    }

    public class PredictionPointDto
    {
        // Make sure these names match the column aliases in your SQL query (e.g., tpd.tbl_project_id as ProjectId)
        // If you don't alias, they must match the original column names.
        // For raw SQL, it's often safer to alias if C# property names differ.
        // Example: tpd.tbl_project_id as ProjectId
        public int tbl_project_id { get; set; } // Matches tpd.tbl_project_id
        public float lat { get; set; }        // Matches tpd.lat
        public float lon { get; set; }        // Matches tpd.lon
        public float? rsrp { get; set; }
        public float? rsrq { get; set; }
        public float? sinr { get; set; }
        public string? band { get; set; }
        public string? earfcn { get; set; }
    }
   
    //table data
    public class tbl_network_log
    {

        public int id { get; set; }
        public int session_id { get; set; }
        public DateTime? timestamp { get; set; }
        public float? lat { get; set; }
        public float? lon { get; set; }
        public int? battery { get; set; }
        public string? dls { get; set; }
        public string? uls { get; set; }
        public string? call_state { get; set; }
        public string? hotspot { get; set; }
        public string? apps { get; set; }
        public int? num_cells { get; set; }
        public string? network { get; set; }//technology
        public int? m_mcc { get; set; }
        public int? m_mnc { get; set; }
        public string? m_alpha_long { get; set; }//provider
        public string? m_alpha_short { get; set; }
        public string? mci { get; set; }
        public string? pci { get; set; }
        public string? tac { get; set; }
        public string? earfcn { get; set; }//drpdown
        public float? rssi { get; set; }
        public float? rsrp { get; set; }
        public float? rsrq { get; set; }
        public float? sinr { get; set; }
        public string? total_rx_kb { get; set; }
        public string? total_tx_kb { get; set; }
        public float? mos { get; set; }
        public float? jitter { get; set; }
        public float? latency { get; set; }
        public float? packet_loss { get; set; }
        public string? dl_tpt { get; set; }
        public string? ul_tpt { get; set; }
        public string? volte_call { get; set; }
        public string? band { get; set; }
        public float? cqi { get; set; }
        public string? bler { get; set; }
        public string? primary_cell_info_1 { get; set; }
        public string? primary_cell_info_2 { get; set; }
        public string? all_neigbor_cell_info { get; set; }
        public string? image_path { get; set; }
        public int? polygon_id { get; set; }

    }

    //api structute
    public class log_network
    {
        public string? timestamp { get; set; }
        public string? lat { get; set; }
        public string? lon { get; set; }
        public string? battery { get; set; }
        public string? dls { get; set; }
        public string? uls { get; set; }
        public string? call_state { get; set; }
        public string? hotspot { get; set; }
        public string? apps { get; set; }
        public string? num_cells { get; set; }
        public string? network { get; set; }
        public string? m_mcc { get; set; }
        public string? m_mnc { get; set; }
        public string? m_alpha_long { get; set; }
        public string? m_alpha_short { get; set; }
        public string? mci { get; set; }
        public string? pci { get; set; }
        public string? tac { get; set; }
        public string? earfcn { get; set; }
        public string? rssi { get; set; }
        public string? rsrp { get; set; }
        public string? rsrq { get; set; }
        public string? sinr { get; set; }
        public string? total_rx_kb { get; set; }
        public string? total_tx_kb { get; set; }
        public string? mos { get; set; }
        public string? jitter { get; set; }
        public string? latency { get; set; }
        public string? packet_loss { get; set; }
        public string? dl_tpt { get; set; }
        public string? ul_tpt { get; set; }
        public string? volte_call { get; set; }
        public string? band { get; set; }
        public string? cqi { get; set; }
        public string? bler { get; set; }
        public string? primary_cell_info_1 { get; set; }
        public string? primary_cell_info_2 { get; set; }
        public string? all_neigbor_cell_info { get; set; }
        public string? image_path { get; set; }
        public string? polygon_id { get; set; }
    }
    public class tbl_prediction_data
    {

        public int id { get; set; }
        
        public float? tbl_project_id { get; set; }
        public float? lat { get; set; }
        public float? lon { get; set; }
        public float? rsrp { get; set; }
        public float? rsrq { get; set; }
        public float? sinr { get; set; }
        public string? serving_cell { get; set; }
        public string? azimuth { get; set; }
        public string? tx_power { get; set; }
        public string? height { get; set; }
        public string? band { get; set; }
        public string? earfcn { get; set; }
        public string? reference_signal_power { get; set; }
        public string? pci { get; set; }
        public string? mtilt { get; set; }
        public string? etilt { get; set; }


    }
    public class tbl_project
    {
        public int id { get; set; }
        public string? project_name { get; set; }

        public string? ref_session_id { get; set; }
        public string? from_date { get; set; }
        public string? to_date { get; set; }
        //public Geometry? polygon" geometry DEFAULT NULL,
        public string? provider { get; set; }
        public string? tech { get; set; }
        public string? band { get; set; }
        public string? earfcn { get; set; }
        public string? apps { get; set; }
        public DateTime? created_on { get; set; }
        public DateTime? ended_on { get; set; }
        public int? created_by_user_id { get; set; }
        public string? created_by_user_name { get; set; }
        public int status { get; set; }
    }
    public class tbl_upload_history
    {
        public int id { get; set; }
        public DateTime uploaded_on { get; set; }
        public int file_type { get; set; }
        public string file_name { get; set; }=String.Empty;
        public int uploaded_by { get; set; }
        public string? remarks { get; set; }
        public string? errors { get; set; }
        public short status { get; set; }

        public string polygon_file { get; set; } = String.Empty;
    }
    public class PolygonMatch
    {
        public int id { get; set; }
    }

    public class PolygonDto
    {
        public int id { get; set; }
        public string name { get; set; }
        public string? wkt { get; set; }
    }

    public class thresholds
    {
        public int id { get; set; }
        public int user_id { get; set; }
        public string? rsrp_json { get; set; }
        public string? rsrq_json { get; set; }
        public string? sinr_json { get; set; }
        public string? dl_thpt_json { get; set; }
        public string? ul_thpt_json { get; set; }
        public string? volte_call { get; set; }
        public string? lte_bler_json { get; set; }

        public string? mos_json { get; set; }

        public int? is_default { get; set; }
    }

    public class map_regions
    {
        public int id { get; set; }
        public int? tbl_project_id { get; set; }
        public string? name { get; set; }
        public byte[] region { get; set; } // GEOMETRY column as byte array
        public float? area { get; set; }
        public int status { get; set; }
    }

}