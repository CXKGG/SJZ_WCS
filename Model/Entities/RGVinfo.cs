using SqlSugar;

namespace Model.Entities
{
    // RGV 设备信息表：一台 RGV 对应一条记录（PLC 侧设备信息）
    public class RGVinfo
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }

        public string plccode { get; set; } = "";       // PLC设备编号（唯一标识）
        public string plcname { get; set; } = "";       // PLC设备名称
        public string ipaddress { get; set; } = "";     // ip地址
        public string port { get; set; } = "102";       // 端口（S7默认102）
        public string model { get; set; } = "";         // plc型号（如S71200）
        public string rack { get; set; } = "0";         // 机架号
        public string slot { get; set; } = "1";         // 插槽号
        public string isactive { get; set; } = "True";  // 是否启用
        public string status { get; set; } = "在线";    // 状态：是否在线/异常/离线
        public string taskstatus { get; set; } = "空闲"; // 任务状态：空闲/执行中/已完成/已暂停
        public string lastheartbeat { get; set; } = ""; // 最后心跳时间
    }
}
