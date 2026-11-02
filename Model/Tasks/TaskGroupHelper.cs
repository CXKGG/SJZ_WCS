using System.Collections.Generic;

namespace Model.Tasks
{
    /// <summary>
    /// 任务组换算工具：把「工位号 / 站群 / 组 / PLC / RGV」之间的对应关系全部集中在这里，用 switch 表达。
    /// <para>依据《小车分组》表：组1 = 61xx + 62xx → PLC1（车1、车2），组2 = 63xx + 64xx → PLC2（车3、车4），</para>
    /// <para>组3 = 65xx + 66xx → PLC3（车5、车6），组4 = 67xx → PLC4（车7，独享叠盘机 6707 / 6708）。</para>
    /// </summary>
    public static class TaskGroupHelper
    {
        /// <summary>拆垛工位尾号：拆垛机 1、拆垛机 2（如 6105 / 6106）</summary>
        public const int DestackSide1 = 5;

        /// <summary>拆垛工位尾号：拆垛机 1、拆垛机 2（如 6105 / 6106）</summary>
        public const int DestackSide2 = 6;

        /// <summary>工位号 → 任务组（按站群号百位段：61/62→组1，63/64→组2，65/66→组3，67→组4）</summary>
        /// <param name="stationNo">工位号，如 6105、6206、6705</param>
        public static TaskGroup GetGroupByStation(int stationNo)
        {
            switch (stationNo / 100)
            {
                case 61:
                case 62:
                    return TaskGroup.Group1;
                case 63:
                case 64:
                    return TaskGroup.Group2;
                case 65:
                case 66:
                    return TaskGroup.Group3;
                case 67:
                    return TaskGroup.Group4;
                default:
                    return TaskGroup.Group4;
            }
        }

        /// <summary>判断工位号是否属于拆垛工位（61xx ~ 67xx）</summary>
        /// <param name="stationNo">工位号</param>
        public static bool IsDestackStation(int stationNo)
        {
            switch (stationNo / 100)
            {
                case 61:
                case 62:
                case 63:
                case 64:
                case 65:
                case 66:
                case 67:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>任务组 → PLC 设备编码</summary>
        /// <param name="group">任务组</param>
        public static string GetPlccode(TaskGroup group)
        {
            switch (group)
            {
                case TaskGroup.Group1:
                    return "PLC1";
                case TaskGroup.Group2:
                    return "PLC2";
                case TaskGroup.Group3:
                    return "PLC3";
                case TaskGroup.Group4:
                    return "PLC4";
                default:
                    return "";
            }
        }

        /// <summary>工位号 → 所属 PLC 设备编码；非法工位返回 null</summary>
        /// <param name="stationNo">工位号</param>
        public static string? TryGetPlccodeByStation(int stationNo)
        {
            if (!IsDestackStation(stationNo))
            {
                return null;
            }

            return GetPlccode(GetGroupByStation(stationNo));
        }

        /// <summary>任务组 → 组内站群号（61xx 组1 有 61、62 两个站群；组4 只有 67 一个站群）</summary>
        /// <param name="group">任务组</param>
        public static int[] GetStationGroups(TaskGroup group)
        {
            switch (group)
            {
                case TaskGroup.Group1:
                    return new int[] { 61, 62 };
                case TaskGroup.Group2:
                    return new int[] { 63, 64 };
                case TaskGroup.Group3:
                    return new int[] { 65, 66 };
                case TaskGroup.Group4:
                    return new int[] { 67 };
                default:
                    return new int[] { };
            }
        }

        /// <summary>任务组 → 组内全部拆垛工位号（每个站群 xx05 / xx06）</summary>
        /// <param name="group">任务组</param>
        public static List<int> GetDestackStations(TaskGroup group)
        {
            List<int> stations = new List<int>();
            int[] stationGroups = GetStationGroups(group);
            foreach (int stationGroup in stationGroups)
            {
                stations.Add(stationGroup * 100 + DestackSide1);
                stations.Add(stationGroup * 100 + DestackSide2);
            }

            return stations;
        }

        /// <summary>PLC 设备编码 → 该 PLC 负责的全部拆垛工位号（组4 只有 6705 / 6706）</summary>
        /// <param name="plccode">PLC 设备编码，如 PLC1</param>
        public static List<int> GetDestackStations(string plccode)
        {
            List<int> stations = new List<int>();
            switch (plccode)
            {
                case "PLC1":
                    return GetDestackStations(TaskGroup.Group1);
                case "PLC2":
                    return GetDestackStations(TaskGroup.Group2);
                case "PLC3":
                    return GetDestackStations(TaskGroup.Group3);
                case "PLC4":
                    return GetDestackStations(TaskGroup.Group4);
                default:
                    return stations;
            }
        }

        /// <summary>任务组 → 组内叠盘机工位号（每台 PLC 一组叠盘机：xx07 / xx08；组1~组3 由两车共用，组4 归车7 独享）</summary>
        /// <param name="group">任务组</param>
        public static int[] GetStackerStations(TaskGroup group)
        {
            switch (group)
            {
                case TaskGroup.Group1:
                    return new int[] { 6107, 6108 };
                case TaskGroup.Group2:
                    return new int[] { 6307, 6308 };
                case TaskGroup.Group3:
                    return new int[] { 6507, 6508 };
                case TaskGroup.Group4:
                    return new int[] { 6707, 6708 };
                default:
                    return new int[] { };
            }
        }

        /// <summary>工位号 → 该 PLC 内 DB600 的段下标（61xx/63xx/65xx/67xx → 0，62xx/64xx/66xx → 1）</summary>
        /// <param name="stationNo">工位号</param>
        public static int GetSegmentIndexByStation(int stationNo)
        {
            if (!IsDestackStation(stationNo))
            {
                return -1;
            }

            switch (stationNo / 100)
            {
                case 61:
                case 63:
                case 65:
                case 67:
                    return 0;
                default:
                    return 1;
            }
        }

        /// <summary>工位号 → 执行该工位的 RGV 设备编码（每个站群固定一台车：61xx→车1 … 67xx→车7）</summary>
        /// <param name="stationNo">工位号</param>
        public static string GetRgvNoByStation(int stationNo)
        {
            switch (stationNo / 100)
            {
                case 61:
                    return "RGV01";
                case 62:
                    return "RGV02";
                case 63:
                    return "RGV03";
                case 64:
                    return "RGV04";
                case 65:
                    return "RGV05";
                case 66:
                    return "RGV06";
                case 67:
                    return "RGV07";
                default:
                    return "";
            }
        }

        /// <summary>任务组 → 组内全部 RGV 设备编码（组4 只有车7）</summary>
        /// <param name="group">任务组</param>
        public static string[] GetRgvNos(TaskGroup group)
        {
            switch (group)
            {
                case TaskGroup.Group1:
                    return new string[] { "RGV01", "RGV02" };
                case TaskGroup.Group2:
                    return new string[] { "RGV03", "RGV04" };
                case TaskGroup.Group3:
                    return new string[] { "RGV05", "RGV06" };
                case TaskGroup.Group4:
                    return new string[] { "RGV07" };
                default:
                    return new string[] { };
            }
        }

        /// <summary>
        /// 任务组 → 公共行程范围的目标点（《小车分组》表，相邻两车共用）：
        /// 组1 = 6107/6108/6103/6201，组2 = 6307/6308/6303/6401，
        /// 组3 = 6507/6508/6503/6607，组4 = 6707/6603。
        /// <para>目前只作记录：判断相邻车冲突时用的是「车是否已经推进到回叠盘机/拣选口阶段」，
        /// 等 RGV 协议（实时位置）定了，再用这组目标点做精确范围判断。</para>
        /// </summary>
        /// <param name="group">任务组</param>
        public static int[] GetPublicRangeStations(TaskGroup group)
        {
            switch (group)
            {
                case TaskGroup.Group1:
                    return new int[] { 6107, 6108, 6103, 6201 };
                case TaskGroup.Group2:
                    return new int[] { 6307, 6308, 6303, 6401 };
                case TaskGroup.Group3:
                    return new int[] { 6507, 6508, 6503, 6607 };
                case TaskGroup.Group4:
                    return new int[] { 6707, 6603 };
                default:
                    return new int[] { };
            }
        }

        /// <summary>
        /// 任务组 + 本车 → 公共行程范围内的相邻冲突车（《小车分组》表的「相邻车冲突点」）：
        /// 组1 车1↔车2、组2 车3↔车4、组3 车5↔车6、组4 车6↔车7（车6 属组3、车7 属组4，跨组冲突）。
        /// </summary>
        /// <param name="group">任务组</param>
        /// <param name="rgvNo">本车设备编码，如 RGV01</param>
        public static string[] GetAdjacentRgvNos(TaskGroup group, string rgvNo)
        {
            switch (group)
            {
                case TaskGroup.Group1:
                    if (rgvNo == "RGV01")
                    {
                        return new string[] { "RGV02" };
                    }

                    if (rgvNo == "RGV02")
                    {
                        return new string[] { "RGV01" };
                    }

                    return new string[] { };
                case TaskGroup.Group2:
                    if (rgvNo == "RGV03")
                    {
                        return new string[] { "RGV04" };
                    }

                    if (rgvNo == "RGV04")
                    {
                        return new string[] { "RGV03" };
                    }

                    return new string[] { };
                case TaskGroup.Group3:
                    if (rgvNo == "RGV05")
                    {
                        return new string[] { "RGV06" };
                    }

                    if (rgvNo == "RGV06")
                    {
                        // 车6 同时和组3 的车5、组4 的车7 冲突
                        return new string[] { "RGV05", "RGV07" };
                    }

                    return new string[] { };
                case TaskGroup.Group4:
                    if (rgvNo == "RGV07")
                    {
                        return new string[] { "RGV06" };
                    }

                    return new string[] { };
                default:
                    return new string[] { };
            }
        }

        /// <summary>工位号 → 所属站群的拣选口工位号（xx01），如 6205 → 6201</summary>
        /// <param name="stationNo">工位号</param>
        public static int GetPickStationByStation(int stationNo)
        {
            return (stationNo / 100) * 100 + 1;
        }

        /// <summary>
        /// 拆垛工位 → 叠盘机块下标：尾号 5（xx05）对应叠盘机【0】（xx07），尾号 6（xx06）对应【1】（xx08）。
        /// DB500 叠盘机垒托盘块【0】基址 148、【1】基址 160。
        /// </summary>
        /// <param name="stationNo">拆垛工位号，如 6105、6106</param>
        public static int GetStackerIndexByDestackStation(int stationNo)
        {
            if (stationNo % 10 == DestackSide1)
            {
                return 0;
            }

            return 1;
        }
    }
}
