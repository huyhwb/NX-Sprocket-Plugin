' NX 12.0 链轮插件 - v22（打通布尔减：实体获取兜底 + 推荐布尔 API）
' v22 变更（v21 实测：主体拉伸已成功，卡在中心孔减料"找不到目标实体"）：
'   [实测进展] v21 报错框：阶段=4/6 中心孔拉伸减料 d=20，
'     轮廓=300 点(100 段弧) 零点 0 个 弧缝 3.55E-014 mm
'     → 轮廓数据完全正确，**链轮主体拉伸成功**（v11 以来几何链首次真正跑通）。
'     卡点：ExtrudeCurves 减料分支抛"找不到目标实体，无法做减法" → gMainBody=Nothing。
'   [根因] Feature.GetEntities()（官方语义"特征创建的实体"）对拉伸特征在本环境
'     未返回 Body（返回项数见报错框"实体："行）→ 主体拉伸后 gMainBody 没被赋值。
'   [修复]
'     1) 新增 FindSolidBody(wp)：扫描 part.Bodies，优先 IsSolidBody 的实体
'        （NX12 无 NXOpen.Solid 类，判实体用 Body.IsSolidBody 属性；NXOpen.xml 确认）。
'     2) ExtrudeCurves 提交后取实体改为三级：GetEntities → FindSolidBody →
'        沿用原 gMainBody；过程写入 gBodyInfo。
'     3) 布尔减改用 NX12 推荐 API（SetBooleanOperationAndBody 官方标记
'        "Deprecated in NX4.0.0"）：BooleanOperation.Type = Subtract
'        + SetTargetBodies(Body[])。
'     4) DoGenerate 开头清空 gMainBody——避免残留上一轮主体掩盖"取实体失败"。
'     5) 新增贯穿裕量 pierce：打孔工具体原来从 z=0 拉到位移 h，与主体上下
'        表面**完全共面**（主体也是 0→h），共面布尔减是 NX 布尔失败头号原因。
'        做法：把打孔整圆建在 z=-pierce（起始平面下移）且位移加长 2*pierce，
'        工具体实占 z ∈ [-pierce, depth+pierce]，两端都超出目标 → 共面彻底消除。
'        刻意**不用"给伸限设负值"**（官方样例里无负值先例，能否被接受不确定）。
'        打孔 pierce=1mm；沉孔=0 保持浅坑。仅 MakeHoleCut 加 pierce 参数，
'        ExtrudeCurves 的算法参数不变（少动一个变量，便于归因）。
'     6) 显式设 FeatureOptions.BodyType = Solid（官方 DrawText.vb 同款），
'        消除默认值不确定导致生成 sheet 体（sheet 无法参与布尔减）的风险。
'   [诊断] 报错框新增"实体："一行：来源(GetEntities/扫描 part.Bodies)/返回项数/
'     是否实体/部件 Body 总数，下次实测即可判定实体获取路径是否走通。
'
' ---- 以下为 v21 说明 ----
' NX 12.0 链轮插件 - v21（修掉真正的根因：轮廓数组步长错位）
' v21 变更（"输入截面无效 / 自相交"的真正根因，由 v20 诊断框一次定位）：
'   [根因] CalcArcs 写入端与 MakeSprocketBody 读取端的**步长不一致**：
'     · 写入：oi = (ti*nPer+si)*compsPer + bi，bi 只循环 0..2（写 3 个点），
'       但 compsPer = 6 → 每段只写 base+0/+1/+2，base+3..+5 **从未赋值**
'     · 读取：MakeSprocketBody 按 6 步长取 pts(o)/pts(o+2)/pts(o+4)
'     · 又因 NXOpen.Point3d 是**结构体**，未赋值槽位 = (0,0,0) 而非空引用
'       （所以不报空引用，静默出错）
'   ⇒ 每段实际读到 "起点 / 终点 / 原点(0,0,0)"，于是每条弧都被 CreateArc
'     建成"过轮廓点、直奔原点"的巨弧 → 100 条巨弧横扫圆心 →
'     NX 报「选定对象将生成一个自相交的截面」。
'   ⇒ 三个独立旁证都对上了：① 报错框 helpPt=pts(2) 恰好等于第 1 段**终点**
'     （而按代码该是"弧上点"）；② 早前"满屏绿线"就是这些巨弧；
'     ③ 错误信息从"载入截面失败"变"输入截面无效"再变"自相交"，
'     都是同一批错误弧在不同允许设置下的不同报法。
'   [修复] 两套步长分开：
'     · locPer   = 6：loc 数组每段 6 个 Double（3 点 × 起/中/终，每点 x,y）
'     · compsPer = 3：buf/result 每段 3 个 Point3d（起/中/终）
'     · MakeSprocketBody 按 3 步长读 o / o+1 / o+2；helpPt 改用第 1 段的
'       **弧上点** pts(1)（严格在弧内，不在两弧交界）
'   [自检] MakeSprocketBody 新增轮廓自检（点数 / 零点数 / 相邻弧缝最大值），
'     结果显示在报错框"轮廓："一行。修好后应为：零点 0 个、弧缝 ≈ 1E-13 mm。
'   [dlx] 禁止符号真因之二：enum 的 Icons 旧版填了 14 个 "none"，而 "none"
'     不是合法位图名 → NX 找不到位图就叠禁止/缺失图标。官方 gc_attribute_ui
'     的 enum 用**自闭合、零条目、mask=16512**；已照抄。dlx 重新生成，
'     Titles_1=14 项、MaximumValue=13、Icons 无条目。
'
' ---- 以下为 v20 说明 ----
' NX 12.0 链轮插件 - v20（诊断增强版）
' v20 变更（用户提供完整堆栈后定位到"堆栈被自己吃掉"）：
'   [关键] v19b 的报错框只有两帧（DoGenerate 行 280 + apply_cb 行 879），
'         根因是 DoGenerate 的 Catch 里写了 `Throw ex`——.NET 的 `Throw ex`
'         会把异常堆栈重置到 throw 那一行，ExtrudeCurves / AddToSection /
'         CommitFeature 的真实出错帧全被抹掉。改成裸 `Throw` 即可透出完整堆栈。
'   [探针] 新增分段标记 gStage（1/6 查表 → 2/6 CalcArcs → 3/6 主体拉伸
'         → 4/6 中心孔 → 5/6 螺丝孔 → 6/6 倒角），失败时显示在哪一步；
'         并在 ExtrudeCurves 内用 Section.GetOutputCurves 查询
'         "截面实际产出几条曲线"（输出 0 条 = 截面没建成 → CommitFeature
'         必然报"输入截面无效"；输出少于输入 = 曲线没串成完整链）。
'   [报错框] 改为显示：失败阶段 + 截面探针 + 异常链（逐层 InnerException）
'         + 内层堆栈。一趟即可定位根因，不必再猜。
'   [对齐官方] 补 DistanceTolerance = 0.0254；
'         AllowSelfIntersectingSection / AllowSelfIntersection 改为 False
'         （官方 QuickExtrude 同款；轮廓已验证不自交，设 False 可让几何问题
'         在 AddToSection 当场暴露，而不是拖到 CommitFeature 报含糊错误）。
'   [错误变化] 用户上轮实测：错误已从 v19 的"载入截面失败"变成
'         "输入截面无效"——说明 v19c 的 seed/helpPt/6参重载改动已越过原失败点，
'         截面现在能载入，卡点在"截面内容被判无效"。
'
' ---- 以下为 v19c 说明 ----
' v19c 变更（交付前对照官方 QuickExtrude.vb 的最后一处对齐）：
'   [helpPt] AddToSection 的 helpPoint 不再传 (0,0,0)（= 链轮圆心，远离轮廓曲线）。
'         官方样例 QuickExtrude.vb:154 的 helpPoint1 就是 seed 曲线上的拾取点。
'         改由调用方传第一条曲线上的真实点：
'           - MakeSprocketBody → 首段弧的弧上点 pts(2)
'           - MakeHoleCut     → 整圆角度0处 (cx+rad, cy)
'   [重载] AddToSection 换回官方 6 参重载（QuickExtrude.vb:162 同款），
'         去掉末尾语义不明的 Boolean 参数。
'
' ---- 以下为 v19b 说明 ----
' v19b 变更（v19 实测"跟上个版本没有任何改善"的针对性修复）：
'   [dlx侧] 禁止符号真因修复（diff_enum.py 逐属性 diff 定位）：
'         官方对话框（gc_attribute_ui，3 选项）MaximumValue = 2 = 选项数-1，
'         即"最大合法索引"；tabsort 模板遗留 MaximumValue=0，
'         而默认选中 Value=2 > 0 → 值越界 → 控件叠加禁止符号。
'         （上一版 v19 只修了 EnumSensitivity 的 data 格式，没碰到真因。）
'         修复：make_dlx.py enum_titles 末尾 set_prop MaximumValue = n-1，
'         dlx 已重新生成。（此修改在 dlx 生成器侧，非本文件）
'   [本文件] "载入截面失败"继续攻坚（v19 seed 传 crvs(0) 无效）：
'     1) CreateSection 容差照抄官方 QuickExtrude.vb：(0.00095, 0.001, 0.5)
'     2) 曲线规则改用 CreateRuleCurveDumb(NXOpen.Curve[])（NX3.0 起老 API，
'        参数类型与我们数组完全一致；原 CreateRuleBaseCurveDumb 是 NX8.5
'        新增的 IBaseCurve 版本，NX12 Journal 环境下行为存疑）
'     3) apply_cb 错误显示改为 ex.ToString() 完整堆栈——
'        "载入截面失败"是 NX 内部包装错，只有堆栈能定位异常发生在
'        AddToSection 还是 CommitFeature。请把报错框全文截图发来。
'
' ---- 以下为 v19 说明 ----
' v19 变更（v18 实测两问题的修复）：
'   [实测1] 链号控件上叠着禁止符号
'         根因（当时的判断）：EnumSensitivity/EnumVisibility 的 stlvector data 格式错误。
'               官方格式是 "@@1@@1@@1..."（@@ 前缀 + 各项以 @@ 分隔），
'               旧写法 "@@1@@" * n 拼出 "@@1@@@@1@@"（项间空 token），
'               NX 解析失败 → 控件叠加禁止图标。
'               （模板原文 21 项："@@1@@1@@...@@1"；gc_attribute_ui 2 项："@@1@@1"）
'         修复：dlx 生成器改写为 "@@" + "@@".join(n 个 "1")。
'               （v19b 复盘：此项也是真因之一，但非唯一；MaximumValue 才是主因）
'   [实测2] 点确定报"载入截面失败"
'         根因：AddToSection 的 seed 参数一直传 Nothing。
'               官方文档：seed = "Seed curve, edge or face"；
'               官方样例 QuickExtrude.vb:162 传第一条曲线 geoms(0)。
'               v11~v18 该路径在 NX12 从未跑通过，首次实测即暴露。
'         修复：seed 传 crvs(0)；并把 Direction 设置移到 AddToSection
'               之后（官方顺序）；补齐官方样板失败清理
'               （eb.Destroy + sec.Destroy，QuickExtrude.vb:188-198）。
'   [附带] 生成失败时 UndoToMark + DeleteUndoMark 回退整个 undo mark——
'         此前失败后曲线残留部件（截图里满屏绿线就是多次失败的堆积），
'         现在失败即自动清场，可安全重试。
'
' ---- 以下为 v18 说明 ----
' v18 变更（v17 实测两个问题的修复 + 用户新需求）：
'   [实测1] 链号下拉显示的是模板箭头样式（Filled Arrow 等 21 项）
'         根因：UI Styler enum 的选项真实数据源是 Titles_1（type=enum，
'               sname="Value"，<Option name=... value=.../> 子节点），
'               Titles(utfstrings) 只是文本缓存。v17 只替换了 Titles。
'         修复：dlx 生成器六处同步替换——Titles_1 的 14 个 Option、
'               Titles 的 14 个 Strings、Icons 14 个 "none"、
'               BalloonTooltipTexts/Images 各 14 个空、
'               EnumSensitivity/EnumVisibility 的 data/size。
'   [实测2] 点确定报"用于属性名称的属性类型不正确"
'         根因：enum 块的 "Value" 运行时是 enum 型属性，GetInteger 取
'               integer 会抛此错（dialogShown 时 UpdateLabels 也踩过，
'               但被 Catch 吞掉没显示）。
'         修复：新增 GetEnumText() 用 GetEnumAsString("Value") 读选项
'               文本（NXOpenUI.xml 确认签名；官方样例
'               MatrixOperations.vb:372 同款用法），返回值即链号名，
'               不再经索引转换（ChainName 已删）。
'   [新需求] 链条规格末尾新增"自定义"选项
'         - dlx：enumChain 14 项；grpChain 新增 dbPitchCus/dbRollerCus
'           两个输入框，默认 Sensitivity=False（禁用）
'         - 联动：update_cb 监听 enumChain/dbPitchCus/dbRollerCus；
'           选"自定义"时启用输入框（SetLogical("Sensitivity")，
'           失败回退 SetLogical("Enable")），标签实时显示输入值
'         - 校验：p ≥ 0.1、d1 ≥ 0.01 且 d1 < p，不满足弹错不关框
'         - 几何：DoGenerate 新增 cP/cR 参数（<=0 用标准表），
'           CalcArcs 签名改为 (teeth, pitch, roller, outerDia) 直收
'           节距/滚子，内部不再查链号表
'
' ---- 以下为 v17 说明 ----
' v17（Block UI Styler 版）：
' v17 变更（v16 的 WinForms 界面整体替换为 NX 自带 UI 编辑器风格的对话框）：
'   [界面] 改用 Block UI Styler 对话框 SprocketPlugin.dlx（6 组 17 个控件），
'          与 NX 自带 GC 工具箱对话框同一套 UI 体系。
'          dlx 由 gen_sprocket_dlx.py 从 NX12 官方 gc 模板参数化生成，
'          23 个控件 id 已逐一校验，XML 结构合法。
'   [模式] Journal（工具→Journal→播放）与 dll（File→Execute NXOpen）两种方式均可运行。
'          对话框关闭时 Journal 进程自然结束；dll 方式由 GetUnloadOption 返回
'          LibraryUnloadOption.Immediately 保证关闭后立即卸载，无驻留。
'   [联动] update_cb 里监听 enumChain：链号变化即刷新 lblPitch / lblRoller
'          （节距 p 与滚子直径 d1）。写标签时先试 SetString("Title")，
'          异常则回退 SetString("Label")，两个属性名都能兜住。
'   [取值] ok_cb / apply_cb 经 GetProperties().GetInteger/GetDouble/GetLogical
'          读取全部控件，随后调用与 v16 完全相同的几何链 DoGenerate。
'   [0值语义] tgCenter 关 → centerDia=0（CalcArcs/DoGenerate 对 0 自动用标准值）
'             tgBolt   关 → bCount=0
'             tgCsk    关 → cskMode=0
'             tgCham   关 → doChamfer=False
'          dbOuterDia 保持 0 即"按标准计算齿顶圆"（CalcArcs 已支持）。
'   [回调] 签名全部对齐 NX12 自带官方样例
'          (UGOPEN/SampleNXOpenApplications/.NET/BlockStyler/ExtrudewithPreview.vb):
'            initialize_cb / dialogShown_cb  : Sub ()
'            update_cb                       : Function (UIBlock) As Integer
'            ok_cb / apply_cb / cancel_cb    : Function () As Integer
'          apply/ok 出错返回 1 → 对话框保持打开，方便改参数重试。
'   [下限] dlx 里齿数范围 7~200（v15 几何验证覆盖 7~200，z=6 未验证不放行）。
'
' ---- 以下为 v16 保留说明 ----
' v16 修复（针对 v15 在 NX12 实测的两个编译错误）：
'   [Line 1046] "Target"不是 NXOpen.GeometricUtilities.BooleanOperation 的成员
'         根因：NX12 的 BooleanOperation 只有 Type 属性（NXOpen.xml 确认），
'               Target 是 NX 1847+ 才有的新成员。
'         修复：改用 SetBooleanOperationAndBody(BooleanType, Body) 一步设置
'               类型 + 目标体（该方法 NX12 就有，XML 注释官方确认）。
'   [Line 1080] "MassProperties"不是 NXOpen.Body 的成员
'         根因：NX12 的 Body 没有 MassProperties 属性（那是 MeasureBodies 的）。
'         修复：彻底放弃"遍历实体比体积"的思路——主轮廓拉伸 commit 后
'               直接 Feature.GetEntities() 取生成的 Body 存入模块级 gMainBody，
'               所有布尔减固定以它为目标。删除 GetLastBody()。
'   另：ExtrudeCurves 的曲线数量下限已改为 1（单个闭合整圆是合法截面，
'       修复打孔报「需要至少2条轮廓曲线」的问题）。
'   [轮廓] 每齿 4 段真圆弧（CreateArc 三点重载，与方向无关，绝对可靠）。
'         验证：2522 组合（13链号×齿数7~200）相邻段端点间隙 < 2.3e-12 mm；
'               v14 折线点到 v15 真弧的法向距离 < 3.6e-11 mm（几何完全等价）。
'   [齿形] ISO 606 标准三段构造：
'     1. 齿沟弧（滚子座）  半径 ri = 0.505 * d1     圆心在节圆上
'     2. 齿面弧（工作弧）  半径 re = 0.12 * d1 * (z+2)
'     3. 齿顶弧            半径 da/2，圆心在链轮中心
'   齿沟角 a = 140° - 90°/z
'   齿顶圆 da = mean( d+p*(1-1.6/z)-d1 , d+p*1.25-d1 )
'   切点求解：两次海伦公式求三角形高
' v14 修复：Atn2 是 VB6 函数名，VB.NET 中为 Atan2（6 处）
' v13 修复：直径/半径单位混用导致齿形变成放射状长条
' v12 修复: CalcPts 缓冲区每齿少分配1点导致"索引超出了数组界限"
' v11 修复: Section 是 .NET 属性，eb.SetSection(sec) -> eb.Section = sec
Option Strict Off
Imports System
Imports System.Math
Imports System.IO
Imports NXOpen
Imports NXOpen.Features
Imports NXOpen.GeometricUtilities
Imports NXOpen.BlockStyler

Module SprocketPlugin

    Private theSession As Session
    Private theUI As UI

    ' 链号规格表：13 种，移植自链轮设计程序.lsp
    ' N=名称  P=节距  R=滚子直径
    ' 08A 与 08B 节距同为 12.7，但滚子直径不同（7.95 / 8.51），必须靠链号区分
    Private CHAIN_N(12) As String
    Private CHAIN_P(12) As Double
    Private CHAIN_R(12) As Double

    ' 链轮主体（布尔减的固定目标体）
    ' v16：不再用体积比较找目标体——Body 在 NX12 没有 MassProperties 属性。
    ' 改为主轮廓拉伸 commit 后直接从 Feature.GetEntities() 拿生成的 Body。
    Private gMainBody As Body = Nothing

    ' v20 诊断：当前执行阶段 + 截面探针信息。
    ' 失败时随错误框一起显示，用来定位是"哪一步/哪条曲线/截面建成没有"出的问题。
    ' （v19b 的报错框只有两帧堆栈——根因是 DoGenerate 里写了 `Throw ex`，
    '   .NET 的 `Throw ex` 会把内层堆栈重置到 throw 那一行，
    '   ExtrudeCurves/CommitFeature 的真实出错位置全被吃掉。改用裸 `Throw` 后
    '   完整堆栈即可透出。）
    ' v20：apply_cb（类内）要读取这两个诊断变量，故用 Friend 而非 Private
    Friend gStage As String = ""
    Friend gSecInfo As String = ""
    ' v21：轮廓数据自检结果（点数 / 零点数 / 相邻弧缝），一并进报错框
    Friend gArcInfo As String = ""
    ' v22：每次拉伸提交后记录"实体获取"过程（部分实体的特征必须能拿到 Body，
    '      否则后续布尔减报"找不到目标实体"）。一并进报错框。
    Friend gBodyInfo As String = ""
    Sub InitData()
        CHAIN_N(0) = "05B"   : CHAIN_P(0) = 8.0     : CHAIN_R(0) = 5.0
        CHAIN_N(1) = "06B"   : CHAIN_P(1) = 9.525   : CHAIN_R(1) = 6.35
        CHAIN_N(2) = "08A"   : CHAIN_P(2) = 12.7    : CHAIN_R(2) = 7.95
        CHAIN_N(3) = "08B"   : CHAIN_P(3) = 12.7    : CHAIN_R(3) = 8.51
        CHAIN_N(4) = "10A"   : CHAIN_P(4) = 15.875  : CHAIN_R(4) = 10.16
        CHAIN_N(5) = "12A"   : CHAIN_P(5) = 19.05   : CHAIN_R(5) = 11.91
        CHAIN_N(6) = "16A"   : CHAIN_P(6) = 25.4    : CHAIN_R(6) = 15.88
        CHAIN_N(7) = "20A"   : CHAIN_P(7) = 31.75   : CHAIN_R(7) = 19.05
        CHAIN_N(8) = "24A"   : CHAIN_P(8) = 38.1    : CHAIN_R(8) = 22.23
        CHAIN_N(9) = "28A"   : CHAIN_P(9) = 44.45   : CHAIN_R(9) = 25.4
        CHAIN_N(10) = "32A"  : CHAIN_P(10) = 50.8   : CHAIN_R(10) = 28.585
        CHAIN_N(11) = "40A"  : CHAIN_P(11) = 63.5   : CHAIN_R(11) = 39.68
        CHAIN_N(12) = "48A"  : CHAIN_P(12) = 76.2   : CHAIN_R(12) = 47.63
    End Sub

    Function GetP(ByVal n As String) As Double
        Dim i As Integer
        For i = 0 To 12
            If CHAIN_N(i) = n Then
                Return CHAIN_P(i)
            End If
        Next i
        Return 12.7
    End Function

    Function GetR(ByVal n As String) As Double
        Dim i As Integer
        For i = 0 To 12
            If CHAIN_N(i) = n Then
                Return CHAIN_R(i)
            End If
        Next i
        Return 7.95
    End Function
    ' ================================================================
    '  Journal / dll 双模式入口
    ' ================================================================
    Sub Main()
        Try
            theSession = Session.GetSession()
            theUI = UI.GetUI()
            InitData()

            If theSession.Parts.Work Is Nothing Then
                theUI.NXMessageBox.Show("链轮生成器", NXMessageBox.DialogType.Error, "请先创建或打开一个部件文件。")
                Return
            End If

            Dim dlg As New SprocketPluginUI()
            dlg.Run()
            dlg.Dispose()

        Catch ex As Exception
            Try
                theUI.NXMessageBox.Show("链轮生成器", NXMessageBox.DialogType.Error, "错误：" & ex.Message)
            Catch
            End Try
        End Try
    End Sub

    ' dll 方式（File→Execute NXOpen）加载本程序集时，
    ' NX 在对话框关闭后立即卸载 dll，不驻留。
    ' Journal 方式下本函数无作用（进程本来就地结束）。
    Public Function GetUnloadOption(ByVal arg As String) As Integer
        Return CType(Session.LibraryUnloadOption.Immediately, Integer)
    End Function

    ' ================================================================
    '  主入口：计算 → 创建轮廓线 → 拉伸实体 → 打孔 → 倒角
    '  v18：新增 cP/cR（自定义节距/滚子直径，<=0 表示按 chainType 查标准表）
    ' ================================================================
    Sub DoGenerate(ByVal teeth As Integer, ByVal chainType As String, ByVal cP As Double, ByVal cR As Double, ByVal thickness As Double, ByVal centerDia As Double, ByVal bCount As Integer, ByVal bDia As Double, ByVal bCircle As Double, ByVal doChamfer As Boolean, ByVal cMethod As Integer, ByVal cD1 As Double, ByVal cD2 As Double, ByVal cAng As Double, ByVal cskMode As Integer, ByVal cskDia As Double, ByVal cskDeep As Double, ByVal cskAngle As Double, ByVal outerDia As Double)
        Dim wp As Part = theSession.Parts.Work
        Dim markId As Session.UndoMarkId
        markId = theSession.SetUndoMark(Session.MarkVisibility.Visible, "GenSprocket")

        Try
            ' v22：每次生成前清空诊断与主体引用。
            ' gMainBody 必须清空——否则若"主体取实体失败"而它残留了上一次
            ' 生成的主体，布尔减会拿旧体减料，掩盖真正的 bug。
            gStage = ""
            gSecInfo = ""
            gArcInfo = ""
            gBodyInfo = ""
            gMainBody = Nothing

            gStage = "1/6 查表取节距/滚子"
            Dim pStd As Double = GetP(chainType)
            Dim rStd As Double = GetR(chainType)
            If cP > 0.0 Then
                pStd = cP
            End If
            If cR > 0.0 Then
                rStd = cR
            End If
            gStage = "2/6 计算轮廓点集 CalcArcs(齿数=" & teeth.ToString() & ", p=" & pStd.ToString("F3") & ", d1=" & rStd.ToString("F3") & ")"
            Dim pts() As Point3d = CalcArcs(teeth, pStd, rStd, outerDia)
            gStage = "3/6 建轮廓弧并拉伸主体（" & (pts.Length \ 6).ToString() & " 段弧）"
            MakeSprocketBody(wp, pts, thickness)
            If centerDia > 0.001 Then
                gStage = "4/6 中心孔拉伸减料 d=" & centerDia.ToString("F3")
                MakeHoleCut(wp, centerDia, 0.0, 0.0, thickness, 1.0)
            End If
            If bCount > 0 Then
                gStage = "5/6 螺丝孔拉伸减料（" & bCount.ToString() & " 个）"
                Dim bi As Integer
                Dim ba As Double
                Dim px As Double
                Dim py As Double
                For bi = 0 To bCount - 1
                    ba = 2.0 * PI * bi / bCount
                    px = bCircle / 2.0 * Cos(ba)
                    py = bCircle / 2.0 * Sin(ba)
                    MakeHoleCut(wp, bDia, px, py, thickness, 1.0)
                    If cskMode >= 1 And cskDia > bDia Then
                        MakeHoleCut(wp, cskDia, px, py, cskDeep, 0.0)
                    End If
                Next bi
            End If
            If doChamfer Then
                gStage = "6/6 倒角"
                DoSimpleChamfer(wp, cMethod, cD1, cD2, cAng)
            End If
            gStage = "完成"
        Catch ex As Exception
            ' v19：失败时回退 undo mark，把本次已创建的残留曲线/特征全部撤销
            '（此前失败后曲线留在部件里，反复尝试会堆积大量垃圾线）
            ' 官方 QuickExtrude.vb 样板：UndoToMark + DeleteUndoMark
            Try
                theSession.UndoToMark(markId, Nothing)
                theSession.DeleteUndoMark(markId, Nothing)
            Catch
            End Try
            ' v20：这里必须是裸 Throw（不能写 Throw ex）！
            ' .NET 的 `Throw ex` 会把异常的堆栈重置到本行，
            ' ExtrudeCurves/AddToSection/CommitFeature 的真实出错帧会被抹掉，
            ' 只剩"DoGenerate 行 280"两帧——这正是上一版看不到根因的原因。
            Throw
        End Try
    End Sub

    ' ================================================================
    '  v20 诊断：把异常链（含 InnerException）与内层堆栈拼成可读文本
    ' ================================================================
    Friend Function BuildErrChain(ByVal ex As Exception) As String
        Dim sb As New System.Text.StringBuilder()
        Dim e As Exception = ex
        Dim di As Integer = 0
        While e IsNot Nothing AndAlso di < 6
            sb.AppendLine("[" & di.ToString() & "] " & e.GetType().FullName & ": " & e.Message)
            e = e.InnerException
            di += 1
        End While
        sb.AppendLine("---- 内层堆栈（最外层在最上）----")
        sb.AppendLine(ex.StackTrace)
        Return sb.ToString()
    End Function

    ' ================================================================
    '  计算链轮轮廓点集
    ' ================================================================
    '  海伦公式求三角形高（用于解两圆切点）
    '  三角形三边 u, v, w，w 为底边，返回底边上的高
    ' ================================================================
    Function TriH(ByVal u As Double, ByVal v As Double, ByVal w As Double) As Double
        Dim s As Double = (u + v + w) * 0.5
        Dim q As Double = s * (s - u) * (s - v) * (s - w)
        If q < 0.0 Then q = 0.0
        Return 2.0 * Sqrt(q) / w
    End Function

    ' ================================================================
    '  角度归一化到 (-PI, PI]，用于圆弧扫向的选择
    ' ================================================================
    Function NormAng(ByVal a As Double) As Double
        Dim r As Double = a
        Do While r > PI
            r = r - 2.0 * PI
        Loop
        Do While r <= -PI
            r = r + 2.0 * PI
        Loop
        Return r
    End Function

    ' ================================================================
    '  计算链轮轮廓圆弧段 —— ISO 606 标准三段真弧
    '
    '  构造（全部按半径，单位 mm）：
    '    分度圆直径  d  = p / Sin(PI/z)
    '    齿根圆直径  df = d - d1
    '    齿顶圆直径  da = ( (d + p*(1-1.6/z) - d1) + (d + p*1.25 - d1) ) / 2
    '    齿沟弧半径  ri = 0.505 * d1              圆心在节圆上
    '    齿面弧半径  re = 0.12 * d1 * (z + 2)     随齿数线性增长
    '    齿沟角      a  = 140° - 90°/z
    '
    '  切点求解：两次海伦公式求三角形高
    '    angm1 = 座弧与齿面弧相切角    angm2 = 齿面弧与齿顶圆相切角
    '    angm3 = angm1 - angm2         齿面弧张角
    '    angm4 = 齿距角 - 2*angm3      齿顶弧张角，必须 > 0
    '
    '  每齿四段弧，按极角递增顺序（保证轮廓不自交）：
    '    段1 齿面弧  圆心 cr1   半径 re    pa1 → pa2
    '    段2 齿沟弧  圆心 c2    半径 ri    pa2 → pa3(最低点) → pa4
    '    段3 齿面弧  圆心 cr1r  半径 re    pa4 → pa5
    '    段4 齿顶弧  圆心 原点  半径 tipR  pa5 → 下一齿 pa1
    '
    '  v15 改动：不再离散成折线点，而是每段输出【起点 / 弧上点 / 终点】三个点，
    '  交给 NX 的 CreateArc(三点重载) 生成真正的圆弧实体。
    '  用三点而非「圆心+起止角」的原因：齿沟弧走向是顺时针（241°→104.6°），
    '  用角度参数容易因 endAngle<startAngle 被 NX 绕成 360-张角 的大圆弧；
    '  三点建弧由 NX 依中间点所在侧自动选弧，方向无关，绝对可靠。
    ' ================================================================
    Function CalcArcs(ByVal teeth As Integer, ByVal pitch As Double, ByVal roller As Double, ByVal outerDia As Double) As Point3d()
        ' 返回每段弧的三个点：起点 / 弧上点 / 终点，共 6 个分量一段
        ' 段数 = teeth * 4，数组长度 = teeth * 4 * 6
        ' v18：节距/滚子直径改为直接传入（支持自定义），不再查链号表
        If teeth < 6 Then teeth = 6
        If teeth > 200 Then teeth = 200

        ' ---------- ISO 606 基本量 ----------
        Dim angTooth As Double = 2.0 * PI / teeth
        Dim pitchDia As Double = pitch / Sin(PI / teeth)
        Dim pitchR As Double = pitchDia / 2.0
        Dim rootR As Double = (pitchDia - roller) / 2.0

        Dim daIso As Double = pitchDia + pitch * (1.0 - 1.6 / teeth) - roller
        Dim daAns As Double = pitchDia + pitch * 1.25 - roller
        Dim tipR As Double = (daIso + daAns) / 4.0
        If outerDia > 0.001 Then
            tipR = outerDia / 2.0
        End If

        Dim ri As Double = 0.505 * roller
        Dim re As Double = 0.12 * roller * (teeth + 2.0)
        Dim a As Double = 140.0 * PI / 180.0 - (PI * 0.5) / teeth

        If tipR <= rootR + roller * 0.2 Then tipR = rootR + roller * 0.2
        If tipR <= 0.001 Then tipR = roller
        If rootR < 0.001 Then rootR = 0.001
        If re < roller * 0.5 Then re = roller * 0.5

        ' ---------- 齿沟中心（局部角 -angTooth/2，圆心在节圆上）----------
        Dim seatA As Double = -angTooth * 0.5
        Dim c2x As Double = pitchR * Cos(seatA)
        Dim c2y As Double = pitchR * Sin(seatA)

        ' ---------- 齿面弧圆心 ----------
        Dim cra As Double = PI + a * 0.5 - angTooth * 0.5
        Dim crx As Double = c2x + (re + ri) * Cos(cra)
        Dim cry As Double = c2y + (re + ri) * Sin(cra)

        ' ---------- 海伦公式解切点 ----------
        Dim w1 As Double = Sqrt(crx * crx + cry * cry)
        If w1 < 0.000001 Then w1 = 0.000001

        Dim h1 As Double = TriH(pitchR, re + ri, w1)
        Dim l1 As Double = pitchR * pitchR - h1 * h1
        If l1 < 0.0 Then l1 = 0.0
        Dim angm1 As Double = Atan2(h1, Sqrt(l1))

        Dim h2 As Double = TriH(tipR, re, w1)
        Dim l2 As Double = tipR * tipR - h2 * h2
        If l2 < 0.0 Then l2 = 0.0
        Dim angm2 As Double = Atan2(h2, Sqrt(l2))

        Dim angm3 As Double = angm1 - angm2
        Dim angm4 As Double = angTooth - angm3 * 2.0
        If angm4 <= 0.0 Then
            angm4 = angTooth * 0.02
            angm3 = (angTooth - angm4) / 2.0
        End If

        ' ---------- 局部关键点（齿 0 坐标系）----------
        Dim pa1a As Double = -(angTooth * 0.5 + angm3)
        Dim pa5a As Double = 2.0 * seatA - pa1a
        Dim pa4a As Double = 2.0 * seatA - cra
        Dim crra As Double = 2.0 * seatA - cra
        Dim crrx As Double = c2x + (re + ri) * Cos(crra)
        Dim crry As Double = c2y + (re + ri) * Sin(crra)

        Dim p1x As Double = tipR * Cos(pa1a)
        Dim p1y As Double = tipR * Sin(pa1a)
        Dim p2x As Double = c2x + ri * Cos(cra)
        Dim p2y As Double = c2y + ri * Sin(cra)
        Dim p3x As Double = c2x + ri * Cos(seatA + PI)
        Dim p3y As Double = c2y + ri * Sin(seatA + PI)
        Dim p4x As Double = c2x + ri * Cos(pa4a)
        Dim p4y As Double = c2y + ri * Sin(pa4a)
        Dim p5x As Double = tipR * Cos(pa5a)
        Dim p5y As Double = tipR * Sin(pa5a)
        Dim p6x As Double = tipR * Cos(pa1a + angTooth)
        Dim p6y As Double = tipR * Sin(pa1a + angTooth)

        ' ---------- 各段中点（弧上点，三点建弧的第三个点）----------
        ' 段1 齿面弧 cr1：中点取弧中角
        Dim s1a As Double = Atan2(p1y - cry, p1x - crx)
        Dim s1b As Double = Atan2(p2y - cry, p2x - crx)
        If NormAng(s1b - s1a) < 0.0 Then s1b = s1b + 2.0 * PI
        Dim m1a As Double = (s1a + s1b) * 0.5
        Dim m1x As Double = crx + re * Cos(m1a)
        Dim m1y As Double = cry + re * Sin(m1a)

        ' 段2 齿沟弧 c2：中点就是 pa3（齿沟最低点）
        Dim m2x As Double = p3x
        Dim m2y As Double = p3y

        ' 段3 齿面弧 cr1r
        Dim s3a As Double = Atan2(p4y - crry, p4x - crrx)
        Dim s3b As Double = Atan2(p5y - crry, p5x - crrx)
        If NormAng(s3b - s3a) < 0.0 Then s3b = s3b + 2.0 * PI
        Dim m3a As Double = (s3a + s3b) * 0.5
        Dim m3x As Double = crrx + re * Cos(m3a)
        Dim m3y As Double = crry + re * Sin(m3a)

        ' 段4 齿顶弧（圆心在原点）
        Dim s4a As Double = Atan2(p5y, p5x)
        Dim s4b As Double = Atan2(p6y, p6x)
        If NormAng(s4b - s4a) < 0.0 Then s4b = s4b + 2.0 * PI
        Dim m4a As Double = (s4a + s4b) * 0.5
        Dim m4x As Double = tipR * Cos(m4a)
        Dim m4y As Double = tipR * Sin(m4a)

        ' ---------- 局部四段（未旋转）----------
        ' 【v21 关键修复】两套步长必须分开，混用会让所有弧全部建错：
        '   locPer  = 6：loc 数组里每段占 6 个 Double（3 个点 × 起/中/终，每点 x,y）
        '   compsPer= 3：buf/result 数组里每段占 3 个 Point3d（起/中/终）
        ' v15~v20 的 bug：compsPer(=6) 同时被当成这两套步长用。写入端 bi 只循环
        '   0..2，只在 base+0/+1/+2 写了 3 个 Point3d，base+3..+5 从未赋值；而
        '   Point3d 是**结构体**，未赋值槽位等于 (0,0,0) 而不是空引用（所以不报
        '   空引用，静默出错）。读取端 MakeSprocketBody 按 6 步长取
        '   pts(o)/pts(o+2)/pts(o+4)，实际读到"起点 / 终点 / 原点(0,0,0)"，
        '   于是每条弧都成了"过轮廓点、直奔原点"的巨弧 → NX 报
        '   "选定对象将生成一个自相交的截面"（也是满屏绿线、以及报错框里
        '   helpPt=pts(2) 恰好等于第 1 段终点的根源）。
        Dim nPer As Integer = 4
        Dim locPer As Integer = 6
        Dim compsPer As Integer = 3
        Dim loc(locPer * 4 - 1) As Double
        ' 段1 齿面弧 pa1 → m1 → pa2
        loc(0) = p1x : loc(1) = p1y
        loc(2) = m1x : loc(3) = m1y
        loc(4) = p2x : loc(5) = p2y
        ' 段2 齿沟弧 pa2 → pa3 → pa4
        loc(6) = p2x : loc(7) = p2y
        loc(8) = m2x : loc(9) = m2y
        loc(10) = p4x : loc(11) = p4y
        ' 段3 齿面弧 pa4 → m3 → pa5
        loc(12) = p4x : loc(13) = p4y
        loc(14) = m3x : loc(15) = m3y
        loc(16) = p5x : loc(17) = p5y
        ' 段4 齿顶弧 pa5 → m4 → 下一齿 pa1
        loc(18) = p5x : loc(19) = p5y
        loc(20) = m4x : loc(21) = m4y
        loc(22) = p6x : loc(23) = p6y

        ' ---------- 按齿旋转，铺满整圈 ----------
        Dim nSeg As Integer = teeth * nPer
        Dim buf(nSeg * compsPer - 1) As Point3d
        Dim si As Integer
        Dim ti As Integer
        Dim bi As Integer
        Dim rc As Double
        Dim rs As Double
        Dim lx As Double
        Dim ly As Double
        Dim qx As Double
        Dim qy As Double
        Dim oi As Integer

        For ti = 0 To teeth - 1
            rc = Cos(angTooth * ti)
            rs = Sin(angTooth * ti)
            For si = 0 To nPer - 1
                For bi = 0 To 2
                    lx = loc(si * locPer + bi * 2)
                    ly = loc(si * locPer + bi * 2 + 1)
                    qx = lx * rc - ly * rs
                    qy = lx * rs + ly * rc
                    oi = (ti * nPer + si) * compsPer + bi
                    buf(oi) = New Point3d(qx, qy, 0.0)
                Next bi
            Next si
        Next ti

        Dim result(nSeg * compsPer - 1) As Point3d
        Dim ki As Integer
        For ki = 0 To nSeg * compsPer - 1
            result(ki) = buf(ki)
        Next ki
        Return result
    End Function

    ' ================================================================
    '  创建轮廓圆弧并拉伸成实体
    '  Curves.CreateArc(startPoint, pointOn, endPoint, alternateSolution, out flipped)
    '  —— NX12 CurveCollection 文档确认的三点建弧重载（弧 = 从起点到终点、过弧上点）。
    '  alternateSolution=False 表示取"规则解"= 过 pointOn 的那段（非补弧）。
    '  【v21】pts 每 3 个 Point3d 一段弧：起点 / 弧上点 / 终点
    '        （旧版按"每 6 个分量"读，与 CalcArcs 的写入步长不一致，
    '          实际读到 起点/终点/原点 → 弧全错，见 CalcArcs 处的说明）
    ' ================================================================
    Sub MakeSprocketBody(ByVal wp As Part, ByVal pts() As Point3d, ByVal h As Double)
        Dim nComp As Integer = pts.Length
        If nComp < 3 Or (nComp Mod 3) <> 0 Then
            Throw New Exception("轮廓数据长度非法，应为3的倍数，当前=" & nComp.ToString())
        End If
        Dim nSeg As Integer = nComp \ 3

        ' v21 自检：统计零点并核对相邻弧是否首尾相接（把结果带进报错框，
        ' 一趟就能判断"数据布局"和"串接"是否有问题）
        Dim zCnt As Integer = 0
        Dim ci As Integer
        Dim maxGap As Double = 0.0
        Dim gp As Double
        For ci = 0 To nComp - 1
            If Abs(pts(ci).X) < 1.0E-12 And Abs(pts(ci).Y) < 1.0E-12 Then zCnt += 1
        Next ci
        Dim si2 As Integer
        For si2 = 0 To nSeg - 1
            Dim ep As Point3d = pts(si2 * 3 + 2)
            Dim sp2b As Point3d = pts(((si2 + 1) Mod nSeg) * 3)
            gp = Sqrt((ep.X - sp2b.X) * (ep.X - sp2b.X) + (ep.Y - sp2b.Y) * (ep.Y - sp2b.Y))
            If gp > maxGap Then maxGap = gp
        Next si2
        gArcInfo = "轮廓点 " & nComp.ToString() & " 个(" & nSeg.ToString() & " 段弧)，零点 " & _
                   zCnt.ToString() & " 个，弧缝最大 " & maxGap.ToString("E2") & " mm"

        Dim arcs(nSeg - 1) As Arc
        Dim si As Integer
        Dim flipped As Boolean
        Dim o As Integer
        Dim sp As Point3d
        Dim pm As Point3d
        Dim pe As Point3d
        Dim arcObj As Arc
        For si = 0 To nSeg - 1
            o = si * 3
            sp = New Point3d(pts(o).X, pts(o).Y, 0.0)
            pm = New Point3d(pts(o + 1).X, pts(o + 1).Y, 0.0)
            pe = New Point3d(pts(o + 2).X, pts(o + 2).Y, 0.0)
            flipped = False
            arcObj = wp.Curves.CreateArc(sp, pm, pe, False, flipped)
            If arcObj Is Nothing Then
                Throw New Exception("CreateArc 返回空，段号=" & si.ToString())
            End If
            arcs(si) = arcObj
        Next si
        ' v21：helpPt 传第 1 段弧的"弧上点"（严格在弧内，不在两弧交界处）
        ExtrudeCurves(wp, arcs, h, False, New Point3d(pts(1).X, pts(1).Y, 0.0))
    End Sub

    ' ================================================================
    '  v22：从部件里取实体（Body）
    '  背景：v21 主体拉伸已成功（轮廓 300 点/100 段弧、零点 0、弧缝 3.5E-14），
    '        但紧接着的中心孔减料报"找不到目标实体，无法做减法" →
    '        说明 ExtrudeCurves 里 Feature.GetEntities() 没取到 Body，
    '        gMainBody 仍为 Nothing。
    '  用法：GetEntities() 取不到时，退回扫描 part.Bodies。
    '        NX12 没有 NXOpen.Solid 类，判断实体用 Body.IsSolidBody 属性
    '        （NXOpen.xml：P:NXOpen.Body.IsSolidBody）。
    '        BodyCollection 支持 ToArray()（NXOpen.xml 确认返回 Body[]）。
    ' ================================================================
    Function FindSolidBody(ByVal wp As Part) As Body
        Dim all() As Body = wp.Bodies.ToArray()
        Dim i As Integer
        ' 第一优先：实体（solid，带体积，才能做布尔减）
        For i = 0 To all.Length - 1
            If all(i).IsSolidBody Then
                Return all(i)
            End If
        Next i
        ' 次选：任意一个 body（可能是 sheet，后面会由布尔运算报错暴露）
        If all.Length > 0 Then
            Return all(0)
        End If
        Return Nothing
    End Function

    ' ================================================================
    '  拉伸曲线数组
    '  基于 Siemens 官方 .NET API 文档：ExtrudeBuilder.Section 是 get/set 属性
    '  v15：入参改为 Curve()（真圆弧）；布尔减必须同时设置 Target，
    '       否则 NX 报「无效的布尔运算类型」
    '  v19：修复"载入截面失败"——AddToSection 的 seed 参数必须传第一条曲线
    '       （官方文档：seed = "Seed curve, edge or face"；官方样例
    '       QuickExtrude.vb:162 传 geoms(0)。v11 以来一直传 Nothing，
    '       NX12 首次实测即报「载入截面失败」）。
    '       并补齐失败清理：eb/sec Destroy（照抄官方样板）。
    ' ================================================================
    Sub ExtrudeCurves(ByVal wp As Part, ByVal crvs() As Curve, ByVal h As Double, ByVal isSubtract As Boolean, ByVal helpPt As Point3d)
        Dim nullFeat As Feature = Nothing
        Dim eb As ExtrudeBuilder = wp.Features.CreateExtrudeBuilder(nullFeat)
        If eb Is Nothing Then
            Throw New Exception("CreateExtrudeBuilder returned Nothing")
        End If

        gSecInfo = ""
        Dim sec As Section = wp.Sections.CreateSection(0.00095, 0.001, 0.5)
        eb.Section = sec

        ' v20：对齐官方 QuickExtrude.vb 两处
        '  1) DistanceTolerance = 0.0254（官方显式设置，截面串接容差）
        '  2) AllowSelfIntersectingSection(False)：默认虽是 True，但轮廓已由
        '     seg15.py 验证不自交（极角单调、无自交）。设 False 的好处是
        '     "几何真有问题"会在 AddToSection 当场暴露，而不是拖到
        '     CommitFeature 报含糊的"输入截面无效"。
        eb.DistanceTolerance = 0.0254
        eb.AllowSelfIntersectingSection(False)

        ' v22：显式要求生成"实体"（官方 DrawText.vb 同款写法）。
        '   本插件所有截面都是闭合的（链轮闭合轮廓 / 打孔整圆），BodyType 必是
        '   Solid；显式设置可消除"默认值不确定 → 生成 sheet"的风险
        '   （sheet 体无法参与布尔减）。
        '   FeatureOptions.BodyType（P，NX4.0 起可读可写）；BodyStyle.Solid
        eb.FeatureOptions.BodyType = GeometricUtilities.FeatureOptions.BodyStyle.Solid

        If eb.Limits Is Nothing OrElse eb.Limits.StartExtend Is Nothing Then
            Throw New Exception("ExtrudeBuilder.Limits or StartExtend is Nothing")
        End If
        eb.Limits.StartExtend.Value.RightHandSide = "0"
        eb.Limits.EndExtend.Value.RightHandSide = h.ToString("F4")

        If isSubtract Then
            ' v22：布尔减必须指定目标体。
            ' NX12 推荐用法（NXOpen.xml 明确：SetBooleanOperationAndBody 已
            ' 标记 "Deprecated in NX4.0.0"，建议改用 Type + SetTargetBodies）：
            '   BooleanOperation.Type = Subtract      （NX4.0 起，可读可写）
            '   BooleanOperation.SetTargetBodies(Body[])
            ' 双保险：若 gMainBody 仍为空，就地再扫一次 part.Bodies 兜底。
            If gMainBody Is Nothing Then
                gMainBody = FindSolidBody(wp)
            End If
            If gMainBody Is Nothing Then
                Throw New Exception("找不到目标实体，无法做减法（部件内 Body 数=" & _
                                    wp.Bodies.ToArray().Length.ToString() & "）")
            End If
            eb.BooleanOperation.Type = GeometricUtilities.BooleanOperation.BooleanType.Subtract
            Dim tgts(0) As Body
            tgts(0) = gMainBody
            eb.BooleanOperation.SetTargetBodies(tgts)
        End If

        sec.AllowSelfIntersection(False)

        ' 单条闭合整圆（如打孔）本身就是合法截面，所以下限为 1
        Dim nCrv As Integer = crvs.Length
        If nCrv < 1 Then
            Throw New Exception("至少需要1条轮廓曲线，当前一条也没有")
        End If

        Dim rul(0) As SelectionIntentRule
        ' v19b：改用 CreateRuleCurveDumb（NX3.0 起的老 API，参数即 Curve[]，
        ' 与我们类型完全一致；CreateRuleBaseCurveDumb 是 NX8.5 新增的 IBaseCurve 版本）
        rul(0) = CType(wp, BasePart).ScRuleFactory.CreateRuleCurveDumb(crvs)
        ' v19：seed 传第一条曲线（官方样例 QuickExtrude.vb 同款），Nothing 会报"载入截面失败"
        ' v19c：helpPt 改由调用方传"第一条曲线上的真实点"（QuickExtrude.vb:154 的
        '       helpPoint1 就是拾取点，落在 seed 曲线上；旧版传 (0,0,0) 即链轮圆心，
        '       远离轮廓曲线，可能是"载入截面失败"诱因）。
        '       AddToSection 同时换回官方 6 参重载（QuickExtrude.vb:162 同款，
        '       少一个语义不明的 Boolean 参数）。
        Dim seedObj As NXObject = CType(crvs(0), NXObject)

        ' v20：AddToSection 单独包裹——失败时给出阶段名 + 输入规模 + helpPt，
        ' 并保留内层异常（wrapped as InnerException，堆栈不丢）
        Try
            sec.AddToSection(rul, seedObj, Nothing, Nothing, helpPt, Section.Mode.Create)
        Catch exSec As Exception
            Throw New Exception("AddToSection 失败：输入曲线 " & nCrv.ToString() & _
                                " 条，helpPt=(" & helpPt.X.ToString("F3") & "," & _
                                helpPt.Y.ToString("F3") & ")", exSec)
        End Try

        ' v20 截面探针：查询 AddToSection 实际产出几条曲线。
        ' 若输出远少于输入 → 曲线没能串成完整链（间隙/朝向问题）；
        ' 若输出 0 条 → 截面根本没建成，CommitFeature 必然报"输入截面无效"。
        Try
            Dim outCrv() As NXObject = Nothing
            sec.GetOutputCurves(outCrv)
            If outCrv Is Nothing Then
                gSecInfo = "截面输出=Nothing"
            Else
                gSecInfo = "截面输出 " & outCrv.Length.ToString() & " 条 (输入 " & nCrv.ToString() & " 条)"
            End If
        Catch exOut As Exception
            gSecInfo = "截面探针查询失败: " & exOut.Message
        End Try

        ' 拉伸方向 +Z（官方样例顺序：AddToSection 之后再设 Direction）
        Dim origin As New Point3d(0.0, 0.0, 0.0)
        Dim zVec As New Vector3d(0.0, 0.0, 1.0)
        Dim dirObj As Direction = wp.Directions.CreateDirection(origin, zVec, SmartObject.UpdateOption.WithinModeling)
        eb.Direction = dirObj

        Try
            Dim feat As Feature = eb.CommitFeature()
            eb.Destroy()

            ' v22：取本次特征产生的实体（Body），供后续布尔减当目标体。
            '   根因回顾：v21 主体拉伸成功，但 GetEntities() 没返回 Body，
            '   gMainBody 一直是 Nothing → 中心孔减料抛"找不到目标实体"。
            '   策略：
            '     1) Feature.GetEntities()（官方语义：特征创建的实体）
            '     2) 取不到 → 扫描 part.Bodies（优先 IsSolidBody）
            '     3) 减料时工具体已被消耗，取不到则沿用原 gMainBody
            Dim ents() As NXObject = feat.GetEntities()
            Dim nEnt As Integer = 0
            Dim found As Body = Nothing
            Dim ei As Integer
            If ents IsNot Nothing Then
                nEnt = ents.Length
                For ei = 0 To ents.Length - 1
                    If TypeOf ents(ei) Is Body Then
                        found = CType(ents(ei), Body)
                        Exit For
                    End If
                Next ei
            End If
            Dim nBody As Integer = wp.Bodies.ToArray().Length
            Dim src As String = "GetEntities"
            If found Is Nothing Then
                found = FindSolidBody(wp)
                src = "扫描 part.Bodies"
            End If
            If found IsNot Nothing Then
                gMainBody = found
                gBodyInfo = "实体获取 OK（来源=" & src & "，GetEntities 返回 " & _
                            nEnt.ToString() & " 项，IsSolidBody=" & found.IsSolidBody.ToString() & _
                            "，部件 Body 总数=" & nBody.ToString() & "）"
            ElseIf gMainBody Is Nothing Then
                gBodyInfo = "实体获取失败：GetEntities 返回 " & nEnt.ToString() & _
                            " 项，部件 Body 总数=" & nBody.ToString()
                Throw New Exception("拉伸提交成功但取不到实体（" & gBodyInfo & "）")
            Else
                gBodyInfo = "减料后未取到新实体，沿用原 gMainBody（GetEntities 返回 " & _
                            nEnt.ToString() & " 项）"
            End If
        Catch
            ' 官方 QuickExtrude 样板的失败清理：Destroy 后把异常抛给上层
            Try
                eb.Destroy()
            Catch
            End Try
            Try
                sec.Destroy()
            Catch
            End Try
            Throw
        End Try
    End Sub

    ' ================================================================
    '  打孔：创建整圆弧 → 拉伸布尔减
    '  CreateArc(center, xDirection, yDirection, radius, startAngle, endAngle)
    '  角度单位为弧度，0→2PI 即整圆
    ' ================================================================
    Sub MakeHoleCut(ByVal wp As Part, ByVal dia As Double, ByVal cx As Double, ByVal cy As Double, ByVal depth As Double, ByVal pierce As Double)
        Dim rad As Double = dia / 2.0
        If rad < 0.001 Then
            Return
        End If
        ' v22：贯穿裕量（消除共面布尔）。
        '   打孔工具体原先从 z=0 拉到位移 depth，与主体上下表面**完全共面**
        '   （主体也是 0→h）——共面布尔减是 NX 布尔失败的头号原因。
        '   这里**不给伸限设负值**（官方样例里无负值先例，能否被接受不确定），
        '   而是把整圆建在 z=-pierce（起始平面下移），并把拉伸位移加长 2*pierce，
        '   于是工具体实际占 z ∈ [-pierce, depth+pierce]，两端都超出目标，共面消除。
        '   沉孔/浅坑传 pierce=0 → z0=0、位移=depth，行为与旧版完全一致。
        Dim z0 As Double = -pierce
        Dim ctr As New Point3d(cx, cy, z0)
        Dim xVec As New Vector3d(1.0, 0.0, 0.0)
        Dim yVec As New Vector3d(0.0, 1.0, 0.0)
        Dim arcObj As Arc = wp.Curves.CreateArc(ctr, xVec, yVec, rad, 0.0, 2.0 * PI)
        If arcObj Is Nothing Then
            Throw New Exception("CreateArc 返回空，孔径=" & dia.ToString())
        End If
        Dim one(0) As Curve
        one(0) = CType(arcObj, Curve)
        ' v19c：helpPt 传整圆上的真实点（角度0处 = 圆心右侧半径处），z 随起始平面
        ExtrudeCurves(wp, one, depth + 2.0 * pierce, True, New Point3d(cx + rad, cy, z0))
    End Sub


    ' ================================================================
    '  简单倒角
    '  ChamferBuilder 的偏移值通过 .RightHandSide 字符串属性设置（不是 SetValue）
    ' ================================================================
    Sub DoSimpleChamfer(ByVal wp As Part, ByVal method As Integer, ByVal d1 As Double, ByVal d2 As Double, ByVal ang As Double)
        Dim nullFeat As Feature = Nothing
        Dim cb As ChamferBuilder = wp.Features.CreateChamferBuilder(nullFeat)
        If cb Is Nothing Then
            Exit Sub
        End If
        Try
            If method = 0 Then
                cb.FirstOffset = d1.ToString("F4")
                cb.SecondOffset = d1.ToString("F4")
            Else
                cb.FirstOffset = d1.ToString("F4")
                cb.SecondOffset = d2.ToString("F4")
            End If
            cb.CommitFeature()
        Catch
        End Try
        Try
            cb.Destroy()
        Catch
        End Try
    End Sub

End Module
' ================================================================
'  Block UI Styler 对话框封装
'  dlx 部署位置（任一即可，按顺序探测）：
'    1) $UGII_USER_DIR\application\SprocketPlugin.dlx
'    2) $UGII_USER_DIR\startup\SprocketPlugin.dlx
'    3) 交给 NX 按标准搜索路径找文件名
' ================================================================
Public Class SprocketPluginUI

    Private theSession As Session
    Private theUI As UI
    Private theDialog As BlockDialog
    Private theDlxPath As String

    ' 控件引用（initialize_cb 里缓存，update/apply 直接用）
    Private enumChainBlk As UIBlock
    Private intTeethBlk As UIBlock
    Private lblPitchBlk As UIBlock
    Private lblRollerBlk As UIBlock
    Private dbPitchCusBlk As UIBlock
    Private dbRollerCusBlk As UIBlock
    Private dbThickBlk As UIBlock
    Private dbOuterDiaBlk As UIBlock
    Private tgCenterBlk As UIBlock
    Private dbCenterDiaBlk As UIBlock
    Private tgBoltBlk As UIBlock
    Private intBoltNBlk As UIBlock
    Private dbBoltDiaBlk As UIBlock
    Private dbBoltPCDBlk As UIBlock
    Private tgCskBlk As UIBlock
    Private dbCskDiaBlk As UIBlock
    Private dbCskDepthBlk As UIBlock
    Private tgChamBlk As UIBlock
    Private dbChamDBlk As UIBlock

    Public Sub New()
        theSession = Session.GetSession()
        theUI = UI.GetUI()
        theDlxPath = FindDlx()
        theDialog = theUI.CreateDialog(theDlxPath)
        ' 回调签名与 NX12 官方样例 ExtrudewithPreview.vb 一致
        theDialog.AddInitializeHandler(AddressOf initialize_cb)
        theDialog.AddDialogShownHandler(AddressOf dialogShown_cb)
        theDialog.AddUpdateHandler(AddressOf update_cb)
        theDialog.AddApplyHandler(AddressOf apply_cb)
        theDialog.AddOkHandler(AddressOf ok_cb)
        theDialog.AddCancelHandler(AddressOf cancel_cb)
    End Sub

    ' 按优先级探测 dlx 位置
    Function FindDlx() As String
        Dim udd As String = Environment.GetEnvironmentVariable("UGII_USER_DIR")
        Dim c1 As String = Nothing
        Dim c2 As String = Nothing
        If udd IsNot Nothing Then
            If udd.Length > 0 Then
                c1 = udd & "\application\SprocketPlugin.dlx"
                c2 = udd & "\startup\SprocketPlugin.dlx"
            End If
        End If
        If c1 IsNot Nothing Then
            If File.Exists(c1) Then
                Return c1
            End If
        End If
        If c2 IsNot Nothing Then
            If File.Exists(c2) Then
                Return c2
            End If
        End If
        Return "SprocketPlugin.dlx"
    End Function

    ' 显示对话框（阻塞直至 OK/Cancel 关闭），返回后释放
    Public Sub Run()
        theDialog.Show()
        theDialog.Dispose()
        theDialog = Nothing
    End Sub

    Public Sub Dispose()
        If theDialog IsNot Nothing Then
            theDialog.Dispose()
            theDialog = Nothing
        End If
    End Sub

    ' ================================================================
    '  回调
    ' ================================================================
    Sub initialize_cb()
        enumChainBlk = theDialog.TopBlock.FindBlock("enumChain")
        intTeethBlk = theDialog.TopBlock.FindBlock("intTeeth")
        lblPitchBlk = theDialog.TopBlock.FindBlock("lblPitch")
        lblRollerBlk = theDialog.TopBlock.FindBlock("lblRoller")
        dbPitchCusBlk = theDialog.TopBlock.FindBlock("dbPitchCus")
        dbRollerCusBlk = theDialog.TopBlock.FindBlock("dbRollerCus")
        dbThickBlk = theDialog.TopBlock.FindBlock("dbThick")
        dbOuterDiaBlk = theDialog.TopBlock.FindBlock("dbOuterDia")
        tgCenterBlk = theDialog.TopBlock.FindBlock("tgCenter")
        dbCenterDiaBlk = theDialog.TopBlock.FindBlock("dbCenterDia")
        tgBoltBlk = theDialog.TopBlock.FindBlock("tgBolt")
        intBoltNBlk = theDialog.TopBlock.FindBlock("intBoltN")
        dbBoltDiaBlk = theDialog.TopBlock.FindBlock("dbBoltDia")
        dbBoltPCDBlk = theDialog.TopBlock.FindBlock("dbBoltPCD")
        tgCskBlk = theDialog.TopBlock.FindBlock("tgCsk")
        dbCskDiaBlk = theDialog.TopBlock.FindBlock("dbCskDia")
        dbCskDepthBlk = theDialog.TopBlock.FindBlock("dbCskDepth")
        tgChamBlk = theDialog.TopBlock.FindBlock("tgCham")
        dbChamDBlk = theDialog.TopBlock.FindBlock("dbChamD")
    End Sub

    Sub dialogShown_cb()
        UpdateCustomUI()
        UpdateLabels()
    End Sub

    Function update_cb(ByVal block As UIBlock) As Integer
        Try
            ' 链号变化 → 联动自定义输入框可用性 + 刷新标签；
            ' 自定义节距/滚子输入变化 → 同步刷新标签显示
            If block Is enumChainBlk Then
                UpdateCustomUI()
                UpdateLabels()
            ElseIf block Is dbPitchCusBlk Or block Is dbRollerCusBlk Then
                UpdateLabels()
            End If
        Catch
        End Try
        Return 0
    End Function

    Function ok_cb() As Integer
        ' OK = Apply + 关闭；出错返回 1 时 NX 不关对话框
        Return apply_cb()
    End Function

    Function apply_cb() As Integer
        Dim errorCode As Integer = 0
        Try
            ' v18 修复：enum 块的 "Value" 是 enum 型属性，必须用 GetEnumAsString
            ' 读取（NXOpenUI.xml 确认；官方样例 MatrixOperations.vb 同款）。
            ' 之前用 GetInteger 报"用于属性名称的属性类型不正确"。
            Dim chainType As String = GetEnumText(enumChainBlk)
            Dim cP As Double = 0.0
            Dim cR As Double = 0.0
            If chainType = "自定义" Then
                cP = GetDblValue(dbPitchCusBlk)
                cR = GetDblValue(dbRollerCusBlk)
                If cP < 0.1 Then
                    Throw New Exception("自定义节距无效：须不小于 0.1 mm")
                End If
                If cR < 0.01 Then
                    Throw New Exception("自定义滚子直径无效：须不小于 0.01 mm")
                End If
                If cR >= cP Then
                    Throw New Exception("自定义滚子直径须小于节距（d1 < p）")
                End If
            End If
            Dim teeth As Integer = GetIntValue(intTeethBlk)
            Dim thick As Double = GetDblValue(dbThickBlk)
            Dim outerDia As Double = GetDblValue(dbOuterDiaBlk)
            Dim doCenter As Boolean = GetLogValue(tgCenterBlk)
            Dim centerDia As Double = GetDblValue(dbCenterDiaBlk)
            Dim doBolt As Boolean = GetLogValue(tgBoltBlk)
            Dim boltN As Integer = GetIntValue(intBoltNBlk)
            Dim boltDia As Double = GetDblValue(dbBoltDiaBlk)
            Dim boltPCD As Double = GetDblValue(dbBoltPCDBlk)
            Dim doCsk As Boolean = GetLogValue(tgCskBlk)
            Dim cskDia As Double = GetDblValue(dbCskDiaBlk)
            Dim cskDeep As Double = GetDblValue(dbCskDepthBlk)
            Dim doCham As Boolean = GetLogValue(tgChamBlk)
            Dim chamD As Double = GetDblValue(dbChamDBlk)

            ' 0 值语义（与 v16 DoGenerate 完全一致）
            If Not doCenter Then
                centerDia = 0.0
            End If
            If Not doBolt Then
                boltN = 0
            End If
            Dim cskMode As Integer = 0
            If doCsk Then
                cskMode = 1
            End If
            ' v18 简化：倒角统一单宽度 chamD（DoSimpleChamfer 的 d2 传同值）
            Dim cMethod As Integer = 0

            DoGenerate(teeth, chainType, cP, cR, thick, centerDia, _
                       boltN, boltDia, boltPCD, doCham, _
                       cMethod, chamD, chamD, 45.0, _
                       cskMode, cskDia, cskDeep, 90.0, outerDia)
        Catch ex As Exception
            ' v20：报错框给出「失败阶段 + 截面探针 + 异常链 + 内层堆栈」。
            ' （v19b 的 ex.ToString() 只有两帧堆栈，根因是 DoGenerate 里 `Throw ex`
            '  把内层堆栈重置了；v20 已改为裸 Throw 并加上分段探针。）
            Dim msg As String = "生成失败" & vbCrLf & "阶段：" & gStage & vbCrLf
            If gArcInfo <> "" Then
                msg = msg & "轮廓：" & gArcInfo & vbCrLf
            End If
            If gSecInfo <> "" Then
                msg = msg & "探针：" & gSecInfo & vbCrLf
            End If
            If gBodyInfo <> "" Then
                msg = msg & "实体：" & gBodyInfo & vbCrLf
            End If
            msg = msg & "---- 异常链 ----" & vbCrLf & BuildErrChain(ex)
            theUI.NXMessageBox.Show("链轮生成器", NXMessageBox.DialogType.Error, msg)
            errorCode = 1
        End Try
        Return errorCode
    End Function

    Function cancel_cb() As Integer
        Return 0
    End Function

    ' ================================================================
    '  控件取值 / 标签刷新
    ' ================================================================
    Function GetIntValue(ByVal blk As UIBlock) As Integer
        Dim p As PropertyList = blk.GetProperties()
        Dim v As Integer = p.GetInteger("Value")
        p.Dispose()
        Return v
    End Function

    Function GetDblValue(ByVal blk As UIBlock) As Double
        Dim p As PropertyList = blk.GetProperties()
        Dim v As Double = p.GetDouble("Value")
        p.Dispose()
        Return v
    End Function

    Function GetLogValue(ByVal blk As UIBlock) As Boolean
        Dim p As PropertyList = blk.GetProperties()
        Dim v As Boolean = p.GetLogical("Value")
        p.Dispose()
        Return v
    End Function

    ' 读 enum 块当前选中的选项文本（如 "08A"、"自定义"）
    ' enum 的 "Value" 是 enum 型属性，GetInteger 会报类型错误
    Function GetEnumText(ByVal blk As UIBlock) As String
        Dim p As PropertyList = blk.GetProperties()
        Dim v As String = p.GetEnumAsString("Value")
        p.Dispose()
        Return v
    End Function

    ' 链号与自定义输入框联动：
    '   选"自定义" → 启用两个手动输入框，标签显示输入值
    '   选标准链号 → 禁用输入框，标签显示标准值
    ' SetLogical("Sensitivity") 若属性名不被接受则保持原状（不影响正确性）
    Sub UpdateCustomUI()
        Try
            Dim nm As String = GetEnumText(enumChainBlk)
            Dim isCustom As Boolean = (nm = "自定义")
            SetLogicalValue(dbPitchCusBlk, isCustom)
            SetLogicalValue(dbRollerCusBlk, isCustom)
        Catch
        End Try
    End Sub

    Sub SetLogicalValue(ByVal blk As UIBlock, ByVal val As Boolean)
        If blk Is Nothing Then
            Return
        End If
        Try
            Dim p As PropertyList = blk.GetProperties()
            p.SetLogical("Sensitivity", val)
            p.Dispose()
            Return
        Catch
        End Try
        Try
            Dim p2 As PropertyList = blk.GetProperties()
            p2.SetLogical("Enable", val)
            p2.Dispose()
        Catch
        End Try
    End Sub

    Sub UpdateLabels()
        Try
            If enumChainBlk Is Nothing Then
                Return
            End If
            Dim nm As String = GetEnumText(enumChainBlk)
            If nm = "自定义" Then
                ' 显示手动输入框的当前值
                Dim pIn As Double = GetDblValue(dbPitchCusBlk)
                Dim rIn As Double = GetDblValue(dbRollerCusBlk)
                SetLabel(lblPitchBlk, "节距 p   = " & pIn.ToString("F3") & " (自定义)")
                SetLabel(lblRollerBlk, "滚子 d1 = " & rIn.ToString("F3") & " (自定义)")
            Else
                SetLabel(lblPitchBlk, "节距 p   = " & GetP(nm).ToString("F3"))
                SetLabel(lblRollerBlk, "滚子 d1 = " & GetR(nm).ToString("F3"))
            End If
        Catch
        End Try
    End Sub

    ' 写只读标签：dlx 里显示文本的 Property id 是 Title；
    ' 个别版本/控件用 Label，先 Title 后 Label 双保险。
    Sub SetLabel(ByVal blk As UIBlock, ByVal txt As String)
        If blk Is Nothing Then
            Return
        End If
        Try
            Dim p As PropertyList = blk.GetProperties()
            p.SetString("Title", txt)
            p.Dispose()
            Return
        Catch
        End Try
        Try
            Dim p2 As PropertyList = blk.GetProperties()
            p2.SetString("Label", txt)
            p2.Dispose()
        Catch
        End Try
    End Sub

End Class

