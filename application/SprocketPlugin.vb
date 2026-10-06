' NX 12.0 链轮插件 - v30（倒角旋转：换"关联回转轴"，解掉三次崩溃的真根因）
' v30 变更（v29 实测：草图5 已正常生成，但旋转仍崩「步骤[1g 提交旋转]：内部错误：内存访问违例」）：
'   [真根因 —— 回转轴建错了重载]
'     v26 / v28 / v29 三次都崩在同一处（CommitFeature），前两版误判成"属性顺序""截面"。
'     真正的问题在建轴这一行：
'         wp.Axes.CreateAxis(New Point3d(0,0,0), New Vector3d(0,0,1), WithinModeling)
'     本机 NXBIN\managed\NXOpen.xml 对该重载的注释写得很明确：
'         "Create a **non-associative** axis"（非关联轴）
'     参数说明还补了一句：
'         "This can only be used by **feature update** in modeling."
'     —— 即这个重载**只允许在特征更新流程里用**。拿它去"新建"一个旋转特征时，
'     提交阶段旋转特征引用到一个非法/非持久的轴对象，NX 内核直接报内存访问违例。
'     因为崩在"提交"而不是"建轴"，前面几版全被误导了。
'
'     [正解] 用**关联**重载（本机 NXOpen.xml 三个重载并列，文档写明按 associativity 区分）：
'         dim dirZ = wp.Directions.CreateDirection(New Point3d(0,0,0), New Vector3d(0,0,1), WithinModeling)
'         dim org  = wp.Points.CreatePoint(New Point3d(0,0,0))
'         wp.Axes.CreateAxis(org, dirZ, WithinModeling)      ' Point + Direction
'     依据：官方 TelescopeMirrorCore.cpp 769-776 行（CreateUpdateRevolve）用的就是这个重载；
'           nxjournaling 上实测跑通的 revolve journal 也用它（且用的是 7 参 AddToSection）。
'     另一处对齐：官方是**先 CreateRevolveBuilder、后建/取轴**，本版也跟着改成这个顺序
'     （v29 是先建轴、再建 builder）。
'
'   [备用路径] ChamferCut 现在会先试"主路径"，失败再自动试一次"备用截面规则"：
'         主路径：CreateRuleCurveDumb(Curve[]) + 6 参 AddToSection
'                 （与已跑通的拉伸路径**完全一致**，截面容差也统一为 0.00095/0.001/0.5）
'         备用路径：CreateRuleBaseCurveDumb(IBaseCurve[]) + 7 参 AddToSection(..., False)
'                 （官方 .NET 草图样例 SketchShape\sketch_shape_journal.vb 的同款写法；
'                   NXOpen/Curve.hxx 62 行 + ICurve.hxx 47 行 证实 Curve → ICurve → IBaseCurve，
'                   所以 Curve() 可以直接当 IBaseCurve() 用）
'       两条都失败时，报错会把两条的原因都列出来，一次实测就能定位。
'
'   [诊断] 新增 gChamSecInfo：记录倒角截面探针（截面输出几条曲线 / 用的哪条规则 / 轴类型）
'          与 gChamInfo 一起进信息框。
'
' ---- 以下为 v29 说明（保留）----
' v29 变更（用户要求：把拉伸/旋转用到的曲线都放进草图 ——
'           草图1 外形轮廓、草图2 中心孔、草图3 螺丝通孔、草图4 螺丝沉孔、草图5 倒角轮廓）：
'   [为什么以前放不进去] v27 只设了 PlaneReference / PlaneOption 就 Commit 草图，
'       实测 ActiveSketch.AddGeometry 报「**对象不在草图平面中**」。
'       翻官方样例（本机 NX12 自带，Java 两份 + .NET 一份写法一致）：
'         UGOPEN\SampleNXOpenApplications\Java\QuerySketch\CreateASketch.java      52-105 行
'         UGOPEN\SampleNXOpenApplications\Java\Licensing\LicensingBase.java       109-152 行
'         UGOPEN\SampleNXOpenApplications\.NET\CAE\BeamModeling\BeamModeling.vb   733-826 行
'       结论：**PlaneReference 必须是一个"关联平面"** —— Planes.CreatePlane 出来的
'       只是一个占位对象，还要
'         pl.SetMethod(PlaneTypes.MethodType.Distance)
'         pl.SetGeometry({基准面}) / pl.SetFlip(False) / pl.SetReverseSide(False)
'         pl.Expression.RightHandSide = "0"
'         pl.SetAlternate(PlaneTypes.AlternateType.One)
'         pl.Evaluate()          ← 关键，缺这一步草图不会落在我们给的平面上
'   [基准面] 用 Datums.CreateFixedDatumPlane(原点, 姿态矩阵) 自建三个固定基准面
'       （XY@z=0、XY@z=-1、XZ@y=0），位置完全由代码给定，
'       不依赖部件里 DATUM_CSYS(n) 的命名；惰性创建，只用到的才建。
'   [五个草图]
'       草图1-外形轮廓   100 段弧（XY 平面 z=0）            → 主体拉伸
'       草图2-中心孔     1 个整圆（XY 平面 z=-1）           → 拉伸减料
'       草图3-螺丝通孔   N 个整圆（XY 平面 z=-1）           → **一次**拉伸减料
'                       （原来是"一个孔一次拉伸"，N 个孔产生 N 个特征）
'       草图4-螺丝沉孔   N 个整圆（XY 平面 z=0）            → 一次拉伸减料（仅勾选沉孔时）
'       草图5-倒角轮廓   8 条棱线（XZ 平面 y=0，两端各 4 条）→ 两次旋转减料
'       z=-1 是 v22 的"贯穿裕量"（整圆下移、位移加长，避开与主体底面的共面布尔）。
'   [倒角] v28 实测「步骤[2f 提交旋转]：内部错误：内存访问违例」——当时以为是
'       RevolveBuilder 的**属性设置顺序**不对，改回官方样例的顺序
'       （TelescopeMirrorCore.cpp 769-812 行 CreateUpdateRevolve）：
'         Axis → Limits(Start/End) → Tolerance → Section      （v28 是 Section 在前、Axis 在后）
'       ！v30 已证实：顺序不是根因（v29 改成官方顺序后照样崩），真根因是**建轴用了非关联重载**，
'        详见文件头 v30 段。属性顺序按官方保留，不再回退。
'       并按官方样例补 Section.SetAllowedEntityTypes(Section.AllowTypes.OnlyCurves)。
'       同时去掉 v27/v28 的"哑曲线方式 / 草图方式 两条路 + 兜底"：
'       只剩一条路 —— 截面必进草图（用户明确要求）。
'   [诊断] 新增 gSkInfo：每个草图建了几条曲线、哪一步失败，随报错框一起显示。
'   [容错] 建草图失败**不抛异常**（原因记进 gSkInfo），后续拉伸/旋转仍用
'       建曲线时的那些 Curve 对象照常进行 —— 官方 SketchShape 样例证明
'       曲线加入草图之后对象依然可用（653 行 AddGeometry → 691 行仍拿它当 seed）。
'
' ---- 以下为 v28 说明 ----
' NX 12.0 链轮插件 - v28（倒角：旋转与布尔减拆成两个特征，对齐官方样例）
' v28 变更（v27.1 实测两条路径的失败点都已定位）：
'   [实测报错]
'     ①草图方式  → 步骤[1e 把四条线加入草图（ActiveSketch.AddGeometry）]：
'                  **对象不在草图平面中**
'     ②哑曲线方式 → 步骤[2f 提交特征（CommitFeature）]：
'                  **内部错误：内存访问违例**
'   [①的修法] 草图平面属性的**设置顺序反了**。v27 是
'       PlaneReference = pl → PlaneOption = ExistingPlane，
'       改成 **PlaneOption 先设、PlaneReference 后设** —— 先声明"用已有平面"，
'       再给它平面，落位这一步才会真的用我们给的那个 XZ 平面。
'   [②的修法：根因在于 API 用法与官方样例不一致]
'       v25~v27 一直把布尔减塞进 RevolveBuilder：
'         rb.BooleanOperation.Type = Subtract + rb.BooleanOperation.SetTargetBodies(...)
'       而西门子唯一同时给出 revolve 与布尔减的官方样例里，**revolve 提交时
'       不带布尔**，布尔减是另一个独立的 BooleanBuilder 特征：
'         UGOPEN\SampleNXOpenApplications\C++\CustomFeatures\TelescopeMirror\
'           TelescopeMirrorCore\TelescopeMirrorCore.cpp
'         · 769-812 行 CreateUpdateRevolve：只设 Axis / Limits / Tolerance / Section
'           → CommitFeature()，全程没碰 BooleanOperation。
'         · 200-250 行 CreateUpdateSubstract：
'             bb = Features.CreateBooleanBuilderUsingCollector(booleanFeature)
'             bb.Tolerance = 0.01
'             bb.Operation = Feature.BooleanType.Subtract
'             bb.Target = 目标体
'             coll = ScCollectors.CreateCollector()
'             coll.ReplaceRules({CreateRuleBodyDumb(工具体, True)}, False)
'             bb.ToolBodyCollector = coll
'             bb.Commit()（.NET 为 CommitFeature）
'       官方对 extrude / cylinder 也都显式设 BooleanTypeCreate（第 313 / 470 行），
'       即"这个特征只造新体，不做布尔"。
'   [v28 做法] 与官方完全对齐，把一次提交拆成两个特征：
'       ① 旋转只造工具体：BooleanOperation.Type = BooleanType.Create → CommitFeature
'       ② 取回工具体：CType(feat, Features.BodyFeature).GetBodies()（官方 209-211 行）
'       ③ 独立 BooleanBuilder：Target = 链轮主体，ToolBodyCollector = 工具体规则
'       好处：再崩也能从步骤标签一眼分出是"提交旋转"还是"提交布尔减"。
'   [目标体] 由 DoTurnChamfer **在造任何工具体之前**抓好（gMainBody），
'       上下两端、两种截面方式共用同一个引用 —— 否则旋转出来的环形工具体会混进
'       wp.Bodies，FindSolidBody 可能抓错目标。
'   [路径顺序] 改为：①哑曲线方式（官方 revolve 就是拿原始曲线做截面）→ 成功即止；
'       ②草图方式作为兜底（用户要求保留）。v27 是反过来的。
'   [失败清理] 布尔减失败时会在信息里提醒"模型里可能残留一个环形工具体"。
'   [行数] 1909。
'
' ---- 以下为 v27.1 说明 ----
' v27.1 修复（v27 实测报 Journal syntax error，20 行错，全部同一个根因）：
'   [根因] **`step` 是 VB 保留字**（For ... Step），不能当变量名。
'          截图里 1235/1242/1245/1276/1282…1355 共 20 行报
'          「关键字作为标识符无效 / 应为表达式 / 语法错误」，
'          其实只是 Dim step As String 这一个声明 + 它被引用的 19 处。
'          Journal 引擎在**编译期**就整脚本拒绝，所以看起来"到处都错"。
'   [修复] 全量重命名 step → stepTag（20 处），无其他改动。
'   [预防] 新增离线预检脚本 check_vb_reserved.py（在 WorkBuddy 工作目录）：
'          扫 Dim/Const/Static 变量名、For/For Each 变量、ByVal/ByRef 参数名，
'          与 VB 保留字表（含 Step/Width/Type/Property/Name/Line 等常见坑）比对。
'          以后改完 vb 先跑它，再跑 check_vb_v17.py。
'   [补充] ①1e 加 AddGeometry 前按官方 DrawText.vb 关掉约束推断
'            （Preferences.Sketch.CreateInferredConstraints），加完恢复原值；
'            前后各包 Try，取不到就跳过，绝不让偏好设置带崩整条链。
'          ②文件行数 1754。
'
' ---- 以下为 v27 说明 ----
' v27 变更（v26 实测：上下两端均报「内部错误：内存访问违例」，只报结果定位不到调用点）：
'   [需求] 用户："能不能将线条导入到草图中？" —— 可以，这就是 NX 的原生做法，已改。
'   [新流程·已被 v29 取代，留档] DoTurnChamfer（建回转轴一次）→ TurnChamferOneEnd
'            （算四点、建四条线）→ ChamferRevolveCut（v28 起改名 ChamferCut）：
'            v29 起 TurnChamferOneEnd 拆成 BuildChamferProfile + ChamferCut。
'   [草图写法] 官方依据（本机样例，非猜测）：
'        UGOPEN\SampleNXOpenApplications\.NET\CAE\BeamModeling\BeamModeling.vb 733-826 行
'        UGOPEN\SampleNXOpenApplications\.NET\DrawText\DrawText.vb 133-195 行
'        wp.Sketches.CreateSketchInPlaceBuilder2(Nothing)
'        → wp.Planes.CreatePlane(原点, 法向, UpdateOption.WithinModeling) 得 XZ 平面
'          （法向 +Y，正好含回转轴 Z）
'        → sib.PlaneReference = pl ; sib.PlaneOption = Sketch.PlaneOption.ExistingPlane
'        → sk = CType(sib.Commit(), Sketch) ; sk.Activate(Sketch.ViewReorient.False)
'        → theSession.ActiveSketch.AddGeometry(线) ×4
'        → sk.Deactivate(Sketch.ViewReorient.False, Sketch.UpdateLevel.Model)
'        顺序：**先建线 → 再建草图 → 再建 builder**（官方 DrawText.vb 同序）。
'   [诊断] 每个 API 调用前打步骤标签，失败时报"步骤[xx]：错误信息"：
'        1a 建草图 / 1b 建草图平面 / 1c 提交草图 / 1d 激活草图 / 1e 加入草图 /
'        1f 退出草图 / 2a 建旋转特征 / 2b 设整圈角度 / 2c 设截面 / 2d 设回转轴 /
'        2e 设布尔减 / 2f 提交特征
'   [兜底] 草图方式整条链失败 → 自动再用"哑曲线方式"做一次；两次都失败才报错，
'        并把两边的失败点都列出来。
'   [时序] 回转轴改由 DoTurnChamfer 提前建好并传给两端（一轴上两端共用），
'        避免"builder 开着的时候又去建特征"。建轴失败会单独报。
'
' ---- 以下为 v26 说明 ----
' v26（车削式倒角：截面修正到 XZ 平面；上下两面都倒）
' v26 变更（用户要求"两面都要倒角"；复查 v25 发现两处必须修的问题）：
'   [问题①·真 bug] v25 把三个点写成 Point3d(半径, zFace, 0.0) —— 即截面被放进了
'        z = 0 的 **XY 平面**。而回转轴是 Z 轴，轴线与该平面**垂直**，不共面；
'        NX 旋转特征要求截面平面包含/平行于回转轴（轴线须落在截面平面内）。
'        这种截面是退化的：绕 Z 旋转后每个点只描出自己的圆，得到一张零厚度的
'        平环，无法构成实体 → 提交报错或什么都不出来。
'        正确写法：Point3d(半径, 0.0, 轴向)，即画在 **XZ 平面**（y = 0）内——
'        该平面正好含 Z 轴。用户原话也是"在 XZ 平面上画一个三角形"。
'   [问题②] 工具体原用三角形，其平边恰好落在端面 z = zFace 上 → 与主体端面
'        **共面**。共面布尔减是本环境失败的头号原因（v22 打孔时踩过）。
'        改为**四边形**，把工具体往端面之外多探出 1 mm（同打孔的 pierce）：
'          Q1 = (tipR - face,   0, zFace)   斜边与端面的交点（此处切削量 0）
'          Q2 = (tipR + over,   0, zSlant)  斜边延长到齿顶圆之外
'          Q3 = (tipR + over,   0, zOut)    探出端面 1 mm（避开共面）
'          Q4 = (tipR - face,   0, zOut)    同上
'        斜边 Q1→Q2 斜率 = axial / face：r = tipR 处切掉 axial（轴向长度），
'        r = tipR - face 处切削量为 0（端面长度）。
'        Q2/Q3/Q4 都在"空气"里（齿顶圆之外或端面之外），不参与切削，
'        所以探出不会多切掉任何东西。
'   [两面都倒] 上端面（z = thick）与下端面（z = 0）**各做一次**旋转减料，
'        两个截面互为镜像（topEnd 决定 sgn）。两个端面分别报告成功/失败。
'   [API] CurveCollection.CreateLine(Point3d, Point3d)                      建棱线
'         FeatureCollection.CreateRevolveBuilder(Nothing)                   建旋转特征
'         RevolveBuilder.Section / Axis / Limits / Tolerance / BooleanOperation
'         BasePart.Axes → AxisCollection.CreateAxis(Point3d, Vector3d, UpdateOption)
'         Limits 在 NXOpen.GeometricUtilities 下（StartExtend/EndExtend 是表达式）
'         布尔减：BooleanOperation.Type = Subtract + SetTargetBodies(Body[])
'         用法已对照官方样例 UGOPEN\SampleNXOpenApplications\C++\CustomFeatures\
'         TelescopeMirror\TelescopeMirrorCore\TelescopeMirrorCore.cpp（740-800 行）。
'   [默认值] dlx 组6：dbChamAx 轴向 = 1.0（0.05~100）；dbChamFc 端面 = 5.0（0.05~500）。
'          车削式不受齿廓曲率限制，5.0 做得出来；剩余限制只有两条：
'          轴向 ≤ 板厚一半、端面 ≤ 齿高。
'   [保护] 轴向*2 > 板厚，或 端面 > 齿顶圆半径-0.5 → 直接跳过并说明（会切穿）。
'   [容错] 倒角是最后一步：失败**不回退整个零件**，只把原因记进 gChamInfo，
'          生成成功后用 Information 框提示（上/下端面分别报）。
'
' ---- 以下为 v25 说明（截面平面写错，已被 v26 修正，保留备查）----
' v25：倒角改为车削式（用户指出 v23/v24 的做法不对：要的是**只倒最外层**，即车削）：
'   [旧做法的问题] v23/v24 用的是 NX 的"边倒角"（ChamferBuilder）——沿每条边的
'          两个相邻面各自偏置。链轮外缘由每齿 4 段弧拼成，08A/z=25 实算：
'          齿沟弧半径 4.01 mm（凹边）、齿顶弧宽 1.75 mm。偏置量稍大就报
'          「无法创建倒斜角。端部条件可能使所有面无法正确相连」——几何上做不到。
'   [新做法] 在含回转轴的平面内画截面 → 绕 Z 轴旋转 360° 得回转体 →
'          与主体布尔减。切出来是**回转面**，与齿形无关，尺寸不再受齿廓曲率限制。
'
' ---- 以下为 v24 说明（已被车削式取代，保留备查）----
' v24：把 DoSimpleChamfer 改成自适应（逐级收紧端面 + 试正/反偏置方向），默认 1 x 0.5。
'      方向错了——车削式（回转体布尔减）才是正解。
'
' ---- 以下为 v23 说明 ----
' NX 12.0 链轮插件 - v23（倒角改为非对称"两侧倒角"：轴向 + 端面）
' v23 变更（按需求：原"外缘倒角"是对称 C 角，只有一个宽度；改为两侧分别输入）：
'   [界面] dlx 组6「倒角」由 1 个 double 改为 2 个：dbChamAx / dbChamFc。
'          tgCham「外缘倒角」保留。apply_cb 侧加了两个下限校验（<0.05 报错）。
'   [API]  NX12 的非对称倒角 = ChamferBuilder：
'            Option = ChamferOption.TwoOffsets        （"两个偏置"，不等距）
'            Method = OffsetMethod.EdgesAlongFaces    （常规倒角）
'            FirstOffset / SecondOffset 是**字符串**（表达式）
'         依据官方样例 UGOPEN/SampleNXOpenApplications/.NET/BlockStyler/
'         SelectionExample/SelectionExample.vb 的 addChamfer()（第 514-556 行）：
'         走 SmartCollector(ScCollector) + ScRuleFactory.CreateRuleEdgeDumb(Edge[])
'         直接喂边数组，不需要交互拾取；Tolerance = 0.0254 亦照抄该样例。
'   [选边] 只倒"外缘齿廓"（上下两端面上那一圈轮廓边，约 2x100 条）：
'            ① 两端点 z 相同        → 排除沿 Z 的竖直棱边（弧段分界棱/侧面竖棱）
'            ② 两端点半径 > 齿根半径 → 排除中心孔/螺丝孔/沉孔的内圈边
'          齿根半径由 DoGenerate 从轮廓点集算 minR 传进来。
'   [容错] 倒角是最后一步：失败**不回退整个零件**（主体已经做出来了），
'          只把原因记进 gChamInfo，生成成功后用 Information 框提示；
'          报错框也会带上倒角信息（若在以后某步失败时一并显示）。
'   [注意] 偏置1/偏置2 与"轴向/端面"的对应由 NX 依边两侧相邻面的内部顺序决定。
'          （v24 起自适应会自动把正/反两个方向都试一遍，不必再手工改。）
'
' ---- 以下为 v22 说明 ----
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
    ' v23：倒角阶段的结果/失败原因。**倒角失败不再中断整个生成**（零件已经做出来了），
    '      只把原因记在这里，生成成功后用信息框提示。
    Friend gChamInfo As String = ""
    ' v29：草图阶段信息（每个草图建了几条曲线 / 哪一步失败），并入报错框。
    Friend gSkInfo As String = ""
    ' v30：倒角截面探针（截面实际收到几条曲线 / 用的哪种截面规则 / 轴是哪一种）。
    '   倒角失败时随 gChamInfo 一起进信息框 —— 截面收不到曲线时 CommitFeature 必崩。
    Friend gChamSecInfo As String = ""
    ' v29：草图的三个固定基准面（惰性创建，用到才建；每次生成开始时清空）。
    '   用户要求把拉伸/旋转用到的曲线全部放进命名草图：
    '     草图1 外形轮廓、草图2 中心孔、草图3 螺丝通孔、草图4 螺丝沉孔、草图5 倒角轮廓。
    Friend gDpXY0 As DatumPlane = Nothing     ' XY 平面，过 (0,0,0)   —— 外形轮廓 / 螺丝沉孔
    Friend gDpXYM1 As DatumPlane = Nothing    ' XY 平面，过 (0,0,-1)  —— 中心孔 / 螺丝通孔
    Friend gDpXZ As DatumPlane = Nothing      ' XZ 平面，过 (0,0,0)   —— 倒角轮廓（含回转轴 Z）
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
    ' v23：倒角参数由 cMethod/cD1/cD2/cAng 改为两个长度——轴向 chamAx 与端面 chamFc
    Sub DoGenerate(ByVal teeth As Integer, ByVal chainType As String, ByVal cP As Double, ByVal cR As Double, ByVal thickness As Double, ByVal centerDia As Double, ByVal bCount As Integer, ByVal bDia As Double, ByVal bCircle As Double, ByVal doChamfer As Boolean, ByVal chamAx As Double, ByVal chamFc As Double, ByVal cskMode As Integer, ByVal cskDia As Double, ByVal cskDeep As Double, ByVal cskAngle As Double, ByVal outerDia As Double)
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
            gChamInfo = ""
            ' v29：草图诊断 + 三个基准面缓存一并清空（同一部件里反复生成时不能让
            ' 上一次的基准面/草图为空引用残留，否则布尔减会拿旧对象）
            gSkInfo = ""
            ' v30：倒角截面探针同样每轮清空
            gChamSecInfo = ""
            gMainBody = Nothing
            gDpXY0 = Nothing
            gDpXYM1 = Nothing
            gDpXZ = Nothing

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
            ' 轮廓半径范围：minR ≈ 齿根圆半径，maxR ≈ 齿顶圆半径。
            ' v26 车削式倒角用 maxR（齿顶圆半径，截面从它向半径外取点），minR 仅用于诊断。
            Dim minR As Double = 1.0E+30
            Dim maxR As Double = 0.0
            Dim qi As Integer
            Dim qr As Double
            For qi = 0 To pts.Length - 1
                qr = Sqrt(pts(qi).X * pts(qi).X + pts(qi).Y * pts(qi).Y)
                If qr < minR Then
                    minR = qr
                End If
                If qr > maxR Then
                    maxR = qr
                End If
            Next qi
            gStage = "3/6 建轮廓弧并拉伸主体（" & (pts.Length \ 6).ToString() & " 段弧，齿根半径 " & minR.ToString("F3") & "）"
            MakeSprocketBody(wp, pts, thickness)
            If centerDia > 0.001 Then
                gStage = "4/6 中心孔拉伸减料 d=" & centerDia.ToString("F3") & "（草图2-中心孔）"
                MakeHoleCutMany(wp, centerDia, New Double() {0.0}, New Double() {0.0}, _
                                thickness, 1.0, "草图2-中心孔")
            End If
            If bCount > 0 Then
                gStage = "5/6 螺丝孔（" & bCount.ToString() & " 个：草图3-螺丝通孔 / 草图4-螺丝沉孔）"
                Dim bxs(bCount - 1) As Double
                Dim bys(bCount - 1) As Double
                Dim bi As Integer
                Dim ba As Double
                For bi = 0 To bCount - 1
                    ba = 2.0 * PI * bi / bCount
                    bxs(bi) = bCircle / 2.0 * Cos(ba)
                    bys(bi) = bCircle / 2.0 * Sin(ba)
                Next bi
                ' v29：全部通孔整圆放进 草图3，**一次**拉伸减料
                MakeHoleCutMany(wp, bDia, bxs, bys, thickness, 1.0, "草图3-螺丝通孔")
                ' 沉孔整圆放进 草图4，再一次拉伸减料
                If cskMode >= 1 And cskDia > bDia Then
                    gStage = "5/6 螺丝沉孔（" & bCount.ToString() & " 个：草图4-螺丝沉孔）"
                    MakeHoleCutMany(wp, cskDia, bxs, bys, cskDeep, 0.0, "草图4-螺丝沉孔")
                End If
            End If
            If doChamfer Then
                ' v26 车削式外缘倒角：轴向 chamAx + 端面 chamFc（默认 1 x 5）
                '   XZ 平面内画四边形截面 → 绕 Z 轴 360° 旋转 → 与主体布尔减。
                '   切出来是回转面，与齿形无关，所以尺寸不再受齿廓曲率限制。
                '   **上下两个端面都倒**（DoTurnChamfer 内部对两端各做一次）。
                '   用 maxR（齿顶圆半径）定位截面起点。
                gStage = "6/6 倒角（车削式：轴向 " & chamAx.ToString("F3") & " / 端面 " & chamFc.ToString("F3") & "）"
                DoTurnChamfer(wp, maxR, thickness, chamAx, chamFc)
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
    '  v29：把一组曲线放进一个"就地草图"（NX 原生做法）
    '
    '  【为什么要重写】
    '    v27 只是把 PlaneReference / PlaneOption 设上就 Commit 草图，实测
    '    ActiveSketch.AddGeometry 报「**对象不在草图平面中**」—— 草图并没有落在
    '    我们给的那个平面上。翻官方样例才发现：
    '    **PlaneReference 必须是一个"关联平面"**，光 CreatePlane 出来还不够，
    '    还要把它变成"从某个基准面偏置而来"的平面，并 Evaluate() 一次。
    '
    '  官方依据（本机 NX12 自带样例；Java 两份、.NET 一份写法完全一致）：
    '    UGOPEN\SampleNXOpenApplications\Java\QuerySketch\CreateASketch.java      52-105 行
    '    UGOPEN\SampleNXOpenApplications\Java\Licensing\LicensingBase.java       109-152 行
    '    UGOPEN\SampleNXOpenApplications\.NET\CAE\BeamModeling\BeamModeling.vb   733-826 行
    '
    '  调用顺序（逐行照抄 CreateASketch.java）：
    '    1) 基准面 base = Datums.CreateFixedDatumPlane(原点, 姿态矩阵)
    '    2) sib  = Sketches.CreateSketchInPlaceBuilder2(Nothing)
    '    3) pl   = Planes.CreatePlane(原点, 法向, WithinModeling)
    '       sib.PlaneReference = pl                       ← 先把引用交给 builder
    '    4) pl.SetMethod(PlaneTypes.MethodType.Distance)  ← 再把 pl 变成
    '       pl.SetGeometry({base})                            "从基准面偏置"的平面
    '       pl.SetFlip(False) / pl.SetReverseSide(False)
    '       pl.Expression.RightHandSide = "0"                 偏置量 0 = 与基准面重合
    '       pl.SetAlternate(PlaneTypes.AlternateType.One)
    '       pl.Evaluate()                                 ← 关键：必须求值一次
    '    5) sib.AxisReference / sib.SketchOrigin / sib.AxisOrientation / sib.PlaneOption
    '    6) sk = CType(sib.Commit(), Sketch) → sib.Destroy()
    '    7) sk.Activate(False) → ActiveSketch.AddGeometry(每条曲线) → sk.Deactivate(...)
    '
    '  截面/拉伸仍然用**我们建曲线时的那些 Curve 对象**（不是草图里另取的）：
    '  官方样例 SketchShape\sketch_shape_journal.vb 第 653 行把 line2 加进草图，
    '  第 691 行又拿同一个 line2 当 AddToSection 的 seed —— 加入草图后对象依然可用。
    ' ================================================================
    '  固定基准面：位置由我们给定，不依赖部件里 DATUM_CSYS(n) 的命名。
    '    kind = 0 → XY 平面，过 (0,0,0)   （外形轮廓、螺丝沉孔的截面都在 z=0）
    '    kind = 1 → XY 平面，过 (0,0,-1)  （中心孔/螺丝通孔的工具体从 z=-1 起，
    '                                      这是 v22 的"贯穿裕量"，避开共面布尔）
    '    kind = 2 → XZ 平面，过 (0,0,0)   （倒角截面必须放在含回转轴 Z 的平面内）
    ' ================================================================
    Function GetBaseDatumPlane(ByVal wp As Part, ByVal kind As Integer) As DatumPlane
        Dim m As New Matrix3x3()
        Dim pt As New Point3d(0.0, 0.0, 0.0)
        Dim nm As String = ""

        If kind = 1 Then
            If gDpXYM1 IsNot Nothing Then
                Return gDpXYM1
            End If
            pt = New Point3d(0.0, 0.0, -1.0)
            nm = "草图基准面-XY(-1)"
            m.Xx = 1.0 : m.Xy = 0.0 : m.Xz = 0.0
            m.Yx = 0.0 : m.Yy = 1.0 : m.Yz = 0.0
            m.Zx = 0.0 : m.Zy = 0.0 : m.Zz = 1.0
        ElseIf kind = 2 Then
            If gDpXZ IsNot Nothing Then
                Return gDpXZ
            End If
            nm = "草图基准面-XZ"
            ' 平面法向 = +Y；平面内 X 轴取世界 X 轴，Y 轴取世界 -Z 轴
            ' （右手系校验：X × Y = (1,0,0) × (0,0,-1) = (0,1,0) = Z ✓）
            m.Xx = 1.0 : m.Xy = 0.0 : m.Xz = 0.0
            m.Yx = 0.0 : m.Yy = 0.0 : m.Yz = -1.0
            m.Zx = 0.0 : m.Zy = 1.0 : m.Zz = 0.0
        Else
            If gDpXY0 IsNot Nothing Then
                Return gDpXY0
            End If
            nm = "草图基准面-XY"
            m.Xx = 1.0 : m.Xy = 0.0 : m.Xz = 0.0
            m.Yx = 0.0 : m.Yy = 1.0 : m.Yz = 0.0
            m.Zx = 0.0 : m.Zy = 0.0 : m.Zz = 1.0
        End If

        Dim dp As DatumPlane = Nothing
        Try
            dp = CType(wp.Datums.CreateFixedDatumPlane(pt, m), DatumPlane)
        Catch ex As Exception
            gSkInfo = gSkInfo & "建基准面失败（" & nm & "）：" & ex.Message & vbCrLf
            Return Nothing
        End Try
        If dp Is Nothing Then
            gSkInfo = gSkInfo & "建基准面失败（" & nm & "）：返回空" & vbCrLf
            Return Nothing
        End If
        Try
            dp.SetName(nm)
        Catch
        End Try

        If kind = 1 Then
            gDpXYM1 = dp
        ElseIf kind = 2 Then
            gDpXZ = dp
        Else
            gDpXY0 = dp
        End If
        Return dp
    End Function

    ' 建一个命名草图，并把给定曲线全部放进去。
    '   kind 见 GetBaseDatumPlane 的说明
    ' 成功返回 Sketch；失败返回 Nothing（原因写进 gSkInfo，不抛出——
    ' 草图失败时后续拉伸仍按"哑曲线"照旧进行，不会把整件带崩）。
    Function MakeSketch(ByVal wp As Part, ByVal crvs() As Curve, ByVal skName As String, _
                        ByVal kind As Integer) As Sketch
        If crvs Is Nothing OrElse crvs.Length = 0 Then
            Return Nothing
        End If

        Dim baseDp As DatumPlane = GetBaseDatumPlane(wp, kind)
        If baseDp Is Nothing Then
            gSkInfo = gSkInfo & skName & "：基准面不可用，未建草图" & vbCrLf
            Return Nothing
        End If

        Dim sib As SketchInPlaceBuilder = Nothing
        Dim pl As Plane = Nothing
        Dim sk As Sketch = Nothing
        Dim stepTag As String = "a 建草图构建器"
        Try
            sib = wp.Sketches.CreateSketchInPlaceBuilder2(Nothing)
            If sib Is Nothing Then
                gSkInfo = gSkInfo & skName & "：CreateSketchInPlaceBuilder2 返回空" & vbCrLf
                Return Nothing
            End If

            stepTag = "b 建平面并交给 PlaneReference"
            pl = wp.Planes.CreatePlane(New Point3d(0.0, 0.0, 0.0), _
                                       New Vector3d(0.0, 0.0, 1.0), _
                                       SmartObject.UpdateOption.WithinModeling)
            sib.PlaneReference = pl

            ' 这一步是 v27 缺的：把 pl 变成"从基准面偏置 0"的关联平面并求值，
            ' 否则草图不会落在 pl 上（AddGeometry 会报"对象不在草图平面中"）。
            stepTag = "c 把平面关联到基准面并求值（SetMethod/SetGeometry/Evaluate）"
            pl.SetMethod(PlaneTypes.MethodType.Distance)
            Dim geoms(0) As NXObject
            geoms(0) = CType(baseDp, NXObject)
            pl.SetGeometry(geoms)
            pl.SetFlip(False)
            pl.SetReverseSide(False)
            pl.Expression.RightHandSide = "0"
            pl.SetAlternate(PlaneTypes.AlternateType.One)
            pl.Evaluate()

            stepTag = "d 设草图轴 / 原点 / 平面选项"
            ' 草图原点必须**落在草图平面上**：kind=1 的平面过 (0,0,-1)，其余过 (0,0,0)
            Dim zOrigin As Double = 0.0
            If kind = 1 Then
                zOrigin = -1.0
            End If
            Try
                sib.AxisReference = wp.Directions.CreateDirection(New Point3d(0.0, 0.0, 0.0), _
                                        New Vector3d(1.0, 0.0, 0.0), _
                                        SmartObject.UpdateOption.WithinModeling)
            Catch
            End Try
            Try
                sib.SketchOrigin = wp.Points.CreatePoint(New Point3d(0.0, 0.0, zOrigin))
            Catch
            End Try
            sib.AxisOrientation = AxisOrientation.Horizontal
            Try
                sib.OriginOption = OriginMethod.SpecifyPoint
            Catch
            End Try
            sib.PlaneOption = Sketch.PlaneOption.ExistingPlane

            stepTag = "e 提交草图（Commit）"
            sk = CType(sib.Commit(), Sketch)
            If sk Is Nothing Then
                gSkInfo = gSkInfo & skName & "：Commit 返回空" & vbCrLf
                Return Nothing
            End If
            sib.Destroy()
            sib = Nothing

            Try
                sk.SetName(skName)
            Catch
            End Try

            stepTag = "f 激活草图并加入曲线（ActiveSketch.AddGeometry）"
            sk.Activate(Sketch.ViewReorient.False)
            Dim i As Integer
            For i = 0 To crvs.Length - 1
                theSession.ActiveSketch.AddGeometry(CType(crvs(i), DisplayableObject), _
                                                    Sketch.InferConstraintsOption.InferNoConstraints)
            Next i
            sk.Deactivate(Sketch.ViewReorient.False, Sketch.UpdateLevel.Model)

            gSkInfo = gSkInfo & skName & "（" & crvs.Length.ToString() & " 条曲线）" & vbCrLf
            Return sk
        Catch ex As Exception
            ' 出错时把还开着的草图退出，别影响后面的操作
            Try
                If sk IsNot Nothing AndAlso sk.IsActive Then
                    sk.Deactivate(Sketch.ViewReorient.False, Sketch.UpdateLevel.Model)
                End If
            Catch
            End Try
            gSkInfo = gSkInfo & skName & " 步骤[" & stepTag & "]失败：" & ex.Message & vbCrLf
            Return Nothing
        Finally
            Try
                If sib IsNot Nothing Then
                    sib.Destroy()
                End If
            Catch
            End Try
        End Try
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
        ' v29：外形轮廓曲线放进「草图1-外形轮廓」（XY 平面 z=0）。
        ' 拉伸截面仍然用 arcs 里这些 Curve 对象（官方 SketchShape 样例证明
        ' 曲线加入草图后对象依旧可用）。
        Dim pcrvs(nSeg - 1) As Curve
        Dim mi As Integer
        For mi = 0 To nSeg - 1
            pcrvs(mi) = CType(arcs(mi), Curve)
        Next mi
        MakeSketch(wp, pcrvs, "草图1-外形轮廓", 0)

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
    '  打孔：创建整圆弧 → （可选）放进草图 → 拉伸布尔减
    '  CreateArc(center, xDirection, yDirection, radius, startAngle, endAngle)
    '  角度单位为弧度，0→2PI 即整圆
    '
    '  v29 重构（用户要求：中心孔曲线进 草图2、螺丝通孔进 草图3、螺丝沉孔进 草图4）：
    '    · 原来"一个孔一次拉伸"，N 个螺丝孔就产生 N 个拉伸特征；
    '      现在**同一组孔的全部整圆放进同一个草图、一次拉伸**，
    '      截面里有多条互不相连的闭合环，NX 会一次减出所有孔（标准用法）。
    '    · 贯穿裕量（v22）保持不变：整圆建在 z=-pierce，拉伸位移 = depth+2*pierce，
    '      工具体实际占 z ∈ [-pierce, depth+pierce]，两端都超出目标，避免共面布尔。
    '      沉孔传 pierce=0 → z0=0、位移=depth，与旧版完全一致。
    ' ================================================================
    Function MakeCircleCurve(ByVal wp As Part, ByVal dia As Double, _
                             ByVal cx As Double, ByVal cy As Double, _
                             ByVal z0 As Double) As Curve
        Dim rad As Double = dia / 2.0
        If rad < 0.001 Then
            Return Nothing
        End If
        Dim ctr As New Point3d(cx, cy, z0)
        Dim xVec As New Vector3d(1.0, 0.0, 0.0)
        Dim yVec As New Vector3d(0.0, 1.0, 0.0)
        Dim arcObj As Arc = wp.Curves.CreateArc(ctr, xVec, yVec, rad, 0.0, 2.0 * PI)
        If arcObj Is Nothing Then
            Throw New Exception("CreateArc 返回空，孔径=" & dia.ToString())
        End If
        Return CType(arcObj, Curve)
    End Function

    ' 一组同直径孔：所有整圆放到一个草图里，然后**一次**拉伸减料。
    '   cx() / cy() 为各孔圆心（长度必须一致）
    '   skName 非空时把整圆放进该草图（kind 由 pierce 决定：>0 → XY(z=-1)，=0 → XY(z=0)）
    Sub MakeHoleCutMany(ByVal wp As Part, ByVal dia As Double, _
                        ByVal cx() As Double, ByVal cy() As Double, _
                        ByVal depth As Double, ByVal pierce As Double, _
                        ByVal skName As String)
        Dim n As Integer = cx.Length
        If n < 1 Then
            Return
        End If
        Dim z0 As Double = -pierce
        Dim crvs(n - 1) As Curve
        Dim i As Integer
        For i = 0 To n - 1
            crvs(i) = MakeCircleCurve(wp, dia, cx(i), cy(i), z0)
        Next i
        If skName <> "" Then
            Dim kind As Integer = 0
            If pierce > 0.0001 Then
                kind = 1
            End If
            MakeSketch(wp, crvs, skName, kind)
        End If
        ' v19c：helpPt 传整圆上的真实点（角度0处 = 圆心右侧半径处），z 随起始平面
        ExtrudeCurves(wp, crvs, depth + 2.0 * pierce, True, New Point3d(cx(0) + dia / 2.0, cy(0), z0))
    End Sub


    ' ================================================================
    '  外缘倒角（v29：车削式 —— 回转体布尔减）
    '
    '  【为什么不用 ChamferBuilder】
    '    v23/v24 走的是 NX 的"边倒角"（ChamferBuilder）：它要沿每条边的两个相邻面
    '    各自偏置。而链轮外缘是每齿 4 段弧拼出来的，齿沟弧半径只有 4.0 mm、
    '    齿顶弧宽只有 1.7 mm（08A/z=25 实算），偏置量稍大就报
    '    「无法创建倒斜角。端部条件可能使所有面无法正确相连」——几何上做不到。
    '
    '  【做法：车削加工】在包含回转轴线的平面 —— XZ 平面（y = 0）—— 内画一个
    '    四边形截面，绕 Z 轴旋转 360° 得到回转体，再与链轮主体**布尔减**。
    '    这正是车床上车外圆倒角的动作：刀尖沿"轴向 axial、径向 face"的斜线进给，
    '    切出来的是**回转面**，与齿形无关 —— 尺寸不再受齿廓曲率限制，
    '    只要轴向不超板厚一半、端面不超齿高即可。
    '
    '  【关键】截面点必须按 (半径, 0, 轴向) 给出（第三个分量才是轴向）。
    '    写成 (半径, 轴向, 0) 会把截面放进 z=0 的 XY 平面，该平面与 Z 轴垂直、
    '    不共面，旋转特征会因截面退化而失败（v25 就是这么错的）。
    '
    '  四边形（在 r-Z 平面内，横轴 r = 半径，纵轴 Z = 轴向）：
    '      Q1 = (tipR - face, zFace)    斜面与端面齐平的一端（此处切削量为 0）
    '      Q2 = (tipR + over, zSlant)   斜边延长到齿顶圆之外的落点
    '      Q3 = (tipR + over, zOut)     提到端面之外 1 mm（避开共面布尔）
    '      Q4 = (tipR - face, zOut)     同上
    '    斜边 Q1→Q2 的斜率 = axial / face，所以：
    '      r = tipR       处切掉 axial  （轴向长度）
    '      r = tipR - face 处切削量为 0 （端面长度）
    '    Q2 / Q3 / Q4 都在"空气"里，不参与切削，只用来保证布尔运算既不与齿顶圆柱面
    '    共面、也不与端面共面（共面布尔是本环境布尔失败的头号原因，v22 打孔时踩过）。
    '
    '  【两个端面】上下各做一次：DoTurnChamfer 一次把两端的截面线都建出来，
    '    全部放进「草图5-倒角轮廓」（同一张草图，XZ 平面），然后分别旋转 + 布尔减。
    '
    '  v29 变更（v28 实测「哑曲线方式 步骤[2f 提交旋转]：内部错误：内存访问违例」）：
    '    · RevolveBuilder 的**属性设置顺序**改回官方样例的顺序：
    '        Axis → Limits → Tolerance → Section
    '      （TelescopeMirrorCore.cpp 769-812 行 CreateUpdateRevolve）
    '      v28 是先 Limits / Tolerance / Section、最后才设 Axis。
    '    · 按官方样例补 Section.SetAllowedEntityTypes(Section.AllowTypes.OnlyCurves)，
    '      截面容差也改用官方 revolve 样例的 0.0095 / 0.01 / 0.5。
    '    · 去掉 v27/v28 的"哑曲线 / 草图 两条路 + 兜底"：只剩一条路 ——
    '      截面必进草图（用户明确要求：拉伸/旋转用到的曲线都要落到命名草图里）。
    '
    '  NX12 API 依据（均在本机 NXBIN\managed\NXOpen.xml 与 UGOPEN\NXOpen\*.hxx 中核对）：
    '    CurveCollection.CreateLine(Point3d, Point3d)                建棱线
    '    FeatureCollection.CreateRevolveBuilder(Feature)             建旋转特征
    '    RevolveBuilder.Axis / Limits / Tolerance / Section（顺序见上）
    '    GeometricUtilities.Limits.StartExtend / EndExtend（Value 为表达式字符串）
    '    BooleanOperation.Type = BooleanType.Create（旋转只造新体，不做布尔）
    '    FeatureCollection.CreateBooleanBuilderUsingCollector + Target + ToolBodyCollector
    '    ScRuleFactory.CreateRuleBodyDumb(Body[], True) / CreateRuleBodyFeature
    '
    '  失败处理：倒角是最后一步，失败**不回退整个零件**，只把原因记进
    '  gChamInfo，由 apply_cb 在生成成功后用 Information 框提示；报错信息带
    '  **步骤标签**，直接指出是哪一句 API 出的问题。
    ' ================================================================
    Sub DoTurnChamfer(ByVal wp As Part, ByVal tipR As Double, ByVal thick As Double, _
                      ByVal axial As Double, ByVal face As Double)
        gChamInfo = ""
        If gMainBody Is Nothing Then
            gMainBody = FindSolidBody(wp)
        End If
        If gMainBody Is Nothing Then
            gChamInfo = "倒角：主实体引用为空（gMainBody = Nothing），已跳过倒角。"
            Return
        End If
        If axial < 0.01 Or face < 0.01 Then
            gChamInfo = "倒角：输入尺寸过小（轴向 " & axial.ToString("F3") & _
                        " / 端面 " & face.ToString("F3") & "），已跳过倒角。"
            Return
        End If
        If face > tipR - 0.5 Then
            gChamInfo = "倒角：端面长度 " & face.ToString("F3") & " 已接近或超过齿顶圆半径 " & _
                        tipR.ToString("F3") & "，会切穿整个齿廓，已跳过倒角。"
            Return
        End If
        If axial * 2.0 > thick Then
            gChamInfo = "倒角：轴向长度 " & axial.ToString("F3") & " 超过板厚 " & _
                        thick.ToString("F3") & " 的一半，上下两个倒角会连成通槽，已跳过倒角。"
            Return
        End If

        ' v30：回转轴不再在这里预建。
        '   原因见文件头 v30 段：旧写法用的是 Axes.CreateAxis(Point3d, Vector3d, …)，
        '   那个重载是"非关联轴"，NXOpen.xml 注明只能用于特征更新 ——
        '   拿它建新旋转特征会在 CommitFeature 里报内存访问违例。
        '   轴改到 ChamferCutOnce 内部、CreateRevolveBuilder **之后**用关联重载创建
        '   （官方 TelescopeMirrorCore.cpp 769-776 行 同款顺序）。

        ' v29：两端的截面线（各 4 条）先全部建出来，一起放进 草图5-倒角轮廓
        Dim helpTop As New Point3d(0.0, 0.0, 0.0)
        Dim helpBot As New Point3d(0.0, 0.0, 0.0)
        Dim linesTop() As Curve = Nothing
        Dim linesBot() As Curve = Nothing
        Try
            linesTop = BuildChamferProfile(wp, tipR, thick, axial, face, True, helpTop)
            linesBot = BuildChamferProfile(wp, tipR, thick, axial, face, False, helpBot)
        Catch exLn As Exception
            gChamInfo = "倒角：建截面棱线失败，已跳过倒角。" & vbCrLf & "  " & exLn.Message
            Return
        End Try

        Dim allLines(7) As Curve
        Dim k As Integer
        For k = 0 To 3
            allLines(k) = linesTop(k)
            allLines(k + 4) = linesBot(k)
        Next k
        MakeSketch(wp, allLines, "草图5-倒角轮廓", 2)

        ' 目标体在造任何工具体之前就抓定（此刻部件里只有链轮主体）
        Dim target As Body = gMainBody
        If target Is Nothing Then
            target = FindSolidBody(wp)
        End If
        If target Is Nothing Then
            gChamInfo = "倒角：找不到目标实体，已跳过倒角。"
            Return
        End If

        gChamSecInfo = ""
        Dim eTop As String = ChamferCut(wp, target, linesTop, helpTop)
        Dim eBot As String = ChamferCut(wp, target, linesBot, helpBot)

        If eTop = "" And eBot = "" Then
            gChamInfo = ""
            Return
        End If

        gChamInfo = "倒角未完全生成（零件其余部分已生成，未回退）：" & vbCrLf
        If eTop <> "" Then
            gChamInfo = gChamInfo & "  · 上端面（z=" & thick.ToString("F3") & "）：" & eTop & vbCrLf
        End If
        If eBot <> "" Then
            gChamInfo = gChamInfo & "  · 下端面（z=0）：" & eBot & vbCrLf
        End If
        If gChamSecInfo <> "" Then
            gChamInfo = gChamInfo & "  · 截面探针：" & gChamSecInfo & vbCrLf
        End If
        gChamInfo = gChamInfo & vbCrLf & _
                    "尺寸：轴向 " & axial.ToString("F3") & " × 端面 " & face.ToString("F3") & _
                    "，齿顶圆半径 " & tipR.ToString("F3") & "，板厚 " & thick.ToString("F3") & vbCrLf & _
                    "提示：端面长度不要超过齿高，轴向长度不超过板厚的一半。"
    End Sub

    ' 建一个端面的车削倒角截面（四边形，4 条棱线，全部在 XZ 平面 y = 0 上）。
    '   topEnd = True  → z = thick 那一端；False → z = 0 那一端
    '   helpPt 回传截面上的一个真实点（pA），供 AddToSection 当 helpPoint
    Function BuildChamferProfile(ByVal wp As Part, ByVal tipR As Double, ByVal thick As Double, _
                                 ByVal axial As Double, ByVal face As Double, _
                                 ByVal topEnd As Boolean, ByRef helpPt As Point3d) As Curve()
        ' 工具体向端面之外多探出的量（避开与端面共面的布尔减，同 v22 打孔的 pierce）
        Dim pierce As Double = 1.0
        ' 半径方向超出齿顶圆的量（避免与外圆柱面共面）；端面很小时按比例缩小
        Dim over As Double = 1.0
        If face < 1.0 Then
            over = face
        End If

        Dim zFace As Double = 0.0
        Dim sgn As Double = 1.0
        If topEnd Then
            zFace = thick
            sgn = -1.0
        End If

        Dim zSlant As Double = zFace + sgn * (axial + axial * over / face)
        Dim zOut As Double = zFace - sgn * pierce

        Dim pA As New Point3d(tipR - face, 0.0, zFace)
        Dim pB As New Point3d(tipR + over, 0.0, zSlant)
        Dim pC As New Point3d(tipR + over, 0.0, zOut)
        Dim pD As New Point3d(tipR - face, 0.0, zOut)

        Dim lines(3) As Curve
        lines(0) = wp.Curves.CreateLine(pA, pB)
        lines(1) = wp.Curves.CreateLine(pB, pC)
        lines(2) = wp.Curves.CreateLine(pC, pD)
        lines(3) = wp.Curves.CreateLine(pD, pA)
        Dim i As Integer
        For i = 0 To 3
            If lines(i) Is Nothing Then
                Throw New Exception("CreateLine 返回空（第 " & i.ToString() & " 条边）")
            End If
        Next i

        helpPt = pA
        Return lines
    End Function

    ' 一次"绕 Z 轴旋转 360° 造出工具体 → 再用独立的布尔减特征切掉"。
    ' 成功返回 ""；失败返回"步骤[xx]：错误信息"（不抛出，由调用方汇总提示）。
    '
    ' 【v30 属性顺序】官方 TelescopeMirrorCore.cpp 767-812 行 CreateUpdateRevolve：
    '     CreateRevolveBuilder
    '       → Axes.CreateAxis(Point, Direction, …)      ← **关联**重载，且在建 builder 之后
    '       → SetAxis
    '       → Limits()->StartExtend()->Value()->SetRightHandSide("0")
    '       → Limits()->EndExtend()->Value()->SetRightHandSide("360")
    '       → SetTolerance
    '       → CreateSection / SetSection / SetAllowedEntityTypes(OnlyCurves) / AddToSection
    '       → CommitFeature
    '   v29 已按这个顺序排过、仍然崩 —— 说明顺序不是根因；
    '   v30 查出真根因是**建轴用了非关联重载**（详见文件头 v30 段），已换成关联重载。
    '
    ' 【为什么布尔减要拆出去】官方**唯一**同时有 revolve 与布尔减的样例里，
    '   revolve 提交时不带布尔；布尔减是另一个独立的 BooleanBuilder 特征
    '   （同文件 200-250 行 CreateUpdateSubstract）。
    '
    ' 【v30 两条路径】ChamferCut 是外壳，内部按顺序试两条截面规则：
    '     主路径   CreateRuleCurveDumb(Curve[]) + 6 参 AddToSection
    '              —— 与已跑通的拉伸路径完全一致
    '     备用路径 CreateRuleBaseCurveDumb(IBaseCurve[]) + 7 参 AddToSection(..., False)
    '              —— 官方 .NET 草图样例 SketchShape\sketch_shape_journal.vb 691 行同款
    '   两条都失败时把两条的原因一起报出来，一次实测即可定位。
    Function ChamferCut(ByVal wp As Part, ByVal target As Body, _
                        ByVal lines() As Curve, ByVal helpPt As Point3d) As String
        Dim e1 As String = ChamferCutOnce(wp, target, lines, helpPt, False)
        If e1 = "" Then
            Return ""
        End If
        Dim e2 As String = ChamferCutOnce(wp, target, lines, helpPt, True)
        If e2 = "" Then
            Return ""
        End If
        Return e1 & vbCrLf & "        （备用截面规则也失败：" & e2 & "）"
    End Function

    ' 单次尝试：绕 Z 轴旋转 360° 造出工具体 → 再用独立的布尔减特征切掉。
    '   useBaseCurveRule = False：截面规则 CreateRuleCurveDumb(Curve[])          （主路径）
    '   useBaseCurveRule = True ：截面规则 CreateRuleBaseCurveDumb(IBaseCurve[])（备用路径）
    Function ChamferCutOnce(ByVal wp As Part, ByVal target As Body, _
                            ByVal lines() As Curve, ByVal helpPt As Point3d, _
                            ByVal useBaseCurveRule As Boolean) As String
        Dim rb As RevolveBuilder = Nothing
        Dim sec As Section = Nothing
        Dim bb As BooleanBuilder = Nothing
        Dim coll As ScCollector = Nothing
        Dim toolFeat As Feature = Nothing
        Dim stepTag As String = "准备"
        Dim errMsg As String = ""
        Dim axDat As NXOpen.Axis = Nothing

        If target Is Nothing Then
            Return "步骤[准备]：目标体（链轮主实体）为空，无法做布尔减"
        End If

        Try
            stepTag = "1a 建旋转特征（Features.CreateRevolveBuilder）"
            Dim nullFeat As Feature = Nothing
            rb = wp.Features.CreateRevolveBuilder(nullFeat)
            If rb Is Nothing Then
                Return "步骤[" & stepTag & "]：CreateRevolveBuilder 返回空"
            End If

            ' ---- 1b：建**关联**回转轴（v30 修正点，三次崩溃的真根因）----
            '   旧写法 wp.Axes.CreateAxis(Point3d, Vector3d, …) 建出来的是"非关联轴"，
            '   NXOpen.xml 注明 "This can only be used by feature update in modeling" ——
            '   拿它建新旋转特征，提交阶段引用到非法轴对象 → 内存访问违例。
            '   改为官方 TelescopeMirrorCore.cpp 769-776 行同款：
            '     Points.CreatePoint + Directions.CreateDirection → Axes.CreateAxis(Point, Direction, …)
            stepTag = "1b 建关联回转轴（Points+Directions → Axes.CreateAxis(Point,Direction)）"
            Dim axPt3 As New Point3d(0.0, 0.0, 0.0)
            Dim axVec3 As New Vector3d(0.0, 0.0, 1.0)
            Dim axDir As NXOpen.Direction = wp.Directions.CreateDirection(axPt3, axVec3, _
                                                                          SmartObject.UpdateOption.WithinModeling)
            If axDir Is Nothing Then
                Return "步骤[" & stepTag & "]：CreateDirection 返回空"
            End If
            Dim axOrg As NXOpen.Point = wp.Points.CreatePoint(axPt3)
            If axOrg Is Nothing Then
                Return "步骤[" & stepTag & "]：CreatePoint 返回空"
            End If
            axDat = wp.Axes.CreateAxis(axOrg, axDir, SmartObject.UpdateOption.WithinModeling)
            If axDat Is Nothing Then
                Return "步骤[" & stepTag & "]：CreateAxis 返回空"
            End If

            stepTag = "1c 赋回转轴（Builder.Axis）"
            rb.Axis = axDat

            stepTag = "1d 设整圈角度 0→360（Builder.Limits）"
            rb.Limits.StartExtend.Value.RightHandSide = "0"
            rb.Limits.EndExtend.Value.RightHandSide = "360"

            stepTag = "1e 设公差（Builder.Tolerance）"
            rb.Tolerance = 0.0254

            ' 明确要"实体"（拉伸那边同样显式指定 Solid）；属性不被支持时忽略，不中断
            Try
                rb.FeatureOptions.BodyType = GeometricUtilities.FeatureOptions.BodyStyle.Solid
            Catch
            End Try

            stepTag = "1f 建截面并加入四条棱线（Section.AddToSection）"
            ' 截面容差与已跑通的拉伸路径保持一致
            sec = wp.Sections.CreateSection(0.00095, 0.001, 0.5)
            rb.Section = sec
            ' 官方样例同款：截面只允许曲线（仅对 dumb / feature 曲线规则有效）
            Try
                sec.SetAllowedEntityTypes(Section.AllowTypes.OnlyCurves)
            Catch
            End Try
            sec.AllowSelfIntersection(False)

            Dim rul(0) As SelectionIntentRule
            If useBaseCurveRule Then
                Dim ibc(lines.Length - 1) As NXOpen.IBaseCurve
                Dim qi As Integer
                For qi = 0 To lines.Length - 1
                    ibc(qi) = CType(lines(qi), NXOpen.IBaseCurve)
                Next qi
                rul(0) = CType(wp, BasePart).ScRuleFactory.CreateRuleBaseCurveDumb(ibc)
            Else
                rul(0) = CType(wp, BasePart).ScRuleFactory.CreateRuleCurveDumb(lines)
            End If
            Dim seedObj As NXObject = CType(lines(0), NXObject)
            sec.AddToSection(rul, seedObj, Nothing, Nothing, helpPt, Section.Mode.Create)

            ' 截面探针：AddToSection 实际收到几条曲线。0 条 → 提交必崩，先报出来。
            Dim ruleName As String = "CreateRuleCurveDumb"
            If useBaseCurveRule Then
                ruleName = "CreateRuleBaseCurveDumb"
            End If
            Try
                Dim outCrv() As NXObject = Nothing
                sec.GetOutputCurves(outCrv)
                If outCrv Is Nothing Then
                    gChamSecInfo = ruleName & " → 截面输出=Nothing"
                Else
                    gChamSecInfo = ruleName & " → 截面输出 " & outCrv.Length.ToString() & _
                                   " 条（输入 " & lines.Length.ToString() & " 条），轴=关联(Point+Direction)"
                End If
            Catch exOut As Exception
                gChamSecInfo = ruleName & " → 截面探针查询失败：" & exOut.Message
            End Try

            stepTag = "1g 声明只建新体（BooleanType.Create）"
            rb.BooleanOperation.Type = GeometricUtilities.BooleanOperation.BooleanType.Create

            stepTag = "1h 提交旋转（CommitFeature → 独立的工具体）"
            toolFeat = rb.CommitFeature()
            If toolFeat Is Nothing Then
                Return "步骤[" & stepTag & "]：CommitFeature 返回空"
            End If

            ' ---- 2) 取回刚转出来的工具体 ----
            stepTag = "2a 取工具体（BodyFeature.GetBodies）"
            Dim toolBodies() As Body = Nothing
            Try
                Dim bf As Features.BodyFeature = CType(toolFeat, Features.BodyFeature)
                toolBodies = bf.GetBodies()
            Catch
                toolBodies = Nothing
            End Try
            If toolBodies IsNot Nothing AndAlso toolBodies.Length = 0 Then
                toolBodies = Nothing
            End If

            ' ---- 3) 独立的布尔减（官方 CreateUpdateSubstract 同款）----
            stepTag = "3a 建布尔减（CreateBooleanBuilderUsingCollector）"
            Dim nullBf As Features.BooleanFeature = Nothing
            bb = wp.Features.CreateBooleanBuilderUsingCollector(nullBf)
            If bb Is Nothing Then
                Return "步骤[" & stepTag & "]：CreateBooleanBuilderUsingCollector 返回空"
            End If
            bb.Tolerance = 0.0254
            bb.Operation = Features.Feature.BooleanType.Subtract
            bb.Target = target

            stepTag = "3b 设工具体收集器（ToolBodyCollector）"
            coll = wp.ScCollectors.CreateCollector()
            Dim rul2(0) As SelectionIntentRule
            If toolBodies IsNot Nothing Then
                ' 官方同款：CreateRuleBodyDumb(工具体数组, True)
                rul2(0) = wp.ScRuleFactory.CreateRuleBodyDumb(toolBodies, True)
            Else
                ' 兜底：直接按"该旋转特征生成的体"来选
                Dim fs(0) As Feature
                fs(0) = toolFeat
                rul2(0) = wp.ScRuleFactory.CreateRuleBodyFeature(fs, True)
            End If
            coll.ReplaceRules(rul2, False)
            bb.ToolBodyCollector = coll

            stepTag = "3c 提交布尔减（CommitFeature）"
            bb.CommitFeature()
            errMsg = ""
        Catch ex As Exception
            errMsg = "步骤[" & stepTag & "]：" & ex.Message
            If toolFeat IsNot Nothing Then
                errMsg = errMsg & vbCrLf & "        （注意：模型里可能残留一个环形工具体，可手工删除）"
            End If
        End Try

        Try
            If rb IsNot Nothing Then
                rb.Destroy()
            End If
        Catch
        End Try
        Try
            If sec IsNot Nothing Then
                sec.Destroy()
            End If
        Catch
        End Try
        Try
            If coll IsNot Nothing Then
                coll.Destroy()
            End If
        Catch
        End Try
        Try
            If bb IsNot Nothing Then
                bb.Destroy()
            End If
        Catch
        End Try
        Return errMsg
    End Function
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
    ' v23：倒角由单一宽度改为两个长度——轴向 / 端面
    Private dbChamAxBlk As UIBlock
    Private dbChamFcBlk As UIBlock

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
        dbChamAxBlk = theDialog.TopBlock.FindBlock("dbChamAx")
        dbChamFcBlk = theDialog.TopBlock.FindBlock("dbChamFc")
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
            ' v23/v24：两侧倒角——轴向长度 + 端面长度（默认 1 x 0.5）
            Dim chamAx As Double = GetDblValue(dbChamAxBlk)
            Dim chamFc As Double = GetDblValue(dbChamFcBlk)
            If chamAx < 0.05 Then
                Throw New Exception("轴向倒角长度无效：须不小于 0.05 mm")
            End If
            If chamFc < 0.05 Then
                Throw New Exception("端面倒角长度无效：须不小于 0.05 mm")
            End If

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
            ' v23：倒角参数改为轴向 / 端面两个长度
            DoGenerate(teeth, chainType, cP, cR, thick, centerDia, _
                       boltN, boltDia, boltPCD, doCham, _
                       chamAx, chamFc, _
                       cskMode, cskDia, cskDeep, 90.0, outerDia)

            ' v23：倒角失败不中断生成（零件已做出），这里用信息框提示原因
            If gChamInfo <> "" Then
                theUI.NXMessageBox.Show("链轮生成器", NXMessageBox.DialogType.Information, gChamInfo)
            End If
        Catch ex As Exception
            ' v20：报错框给出「失败阶段 + 截面探针 + 异常链 + 内层堆栈」。
            ' （v19b 的 ex.ToString() 只有两帧堆栈，根因是 DoGenerate 里 `Throw ex`
            '  把内层堆栈重置了；v20 已改为裸 Throw 并加上分段探针。）
            Dim msg As String = "生成失败" & vbCrLf & "阶段：" & gStage & vbCrLf
            If gArcInfo <> "" Then
                msg = msg & "轮廓：" & gArcInfo & vbCrLf
            End If
            If gSkInfo <> "" Then
                msg = msg & "草图：" & vbCrLf & gSkInfo
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

