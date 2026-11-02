namespace Model.Tasks
{
    /// <summary>
    /// 拆垛任务组：一台 PLC + 若干台 RGV 组成一个任务组（见《小车分组》表和《石家庄四药国龙项目托盘输送编号图》）。
    /// <para>组1 = PLC1 + 车1（61xx）+ 车2（62xx），两车共用叠盘机 6107 / 6108；</para>
    /// <para>组2 = PLC2 + 车3（63xx）+ 车4（64xx），两车共用叠盘机 6307 / 6308；</para>
    /// <para>组3 = PLC3 + 车5（65xx）+ 车6（66xx），两车共用叠盘机 6507 / 6508；</para>
    /// <para>组4 = PLC4 + 车7（67xx），车7 独享一组叠盘机 6707 / 6708。</para>
    /// </summary>
    public enum TaskGroup
    {
        /// <summary>组1：PLC1，车1（61xx）、车2（62xx）</summary>
        Group1 = 1,

        /// <summary>组2：PLC2，车3（63xx）、车4（64xx）</summary>
        Group2 = 2,

        /// <summary>组3：PLC3，车5（65xx）、车6（66xx）</summary>
        Group3 = 3,

        /// <summary>组4：PLC4，车7（67xx，独享一组叠盘机）</summary>
        Group4 = 4
    }
}
