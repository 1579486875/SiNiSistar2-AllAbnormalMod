using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSiNiSistar2.Lc;
using Il2CppSiNiSistar2.Manager;
using Il2CppSiNiSistar2.Manager.Gallery;
using Il2CppSiNiSistar2.Obj;
using Il2CppSiNiSistar2.UI.Gallery;
using Il2CppSystem.Collections.Generic;
using Il2CppSystem.Threading;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AllAbnormalMod;

/// <summary>AllAbnormalMod 主类：管理异常状态面板的生命周期、按需加载与状态等级读写。</summary>
public class Main : MelonMod
{
	// ───────────────────────── try/catch 约定 ─────────────────────────
	// 本文件里很多 catch 是**空**的，这是有意的，不是偷懒：
	// 模组活在游戏自己的主循环里，任何一处把异常抛出去都可能直接打断
	// 游戏的初始化或渲染 —— 那比「某个功能失效」严重得多。
	//
	// 统一原则：
	//   · 调用游戏侧接口（Il2Cpp 互操作）一律包 try：拿不到就当作没有，走降级分支；
	//   · 访问可能为 null 的 Unity 对象前先判空，判空本身失败也吞掉；
	//   · UI 写入失败只影响显示，绝不影响游戏逻辑。
	//
	// 因此**不要**往这些空 catch 里补日志：异常场景下它们可能每帧触发，
	// 而写日志是同步磁盘 IO（MelonLogger 还会抓调用栈），会把卡顿放大成雪崩。
	// 确实需要留痕的地方，都在 catch 外面单独判了状态、并且只打一次。

	private static string _modVersion;

	internal static readonly System.Collections.Generic.List<StatusRow> Rows = new System.Collections.Generic.List<StatusRow>();

	internal static GameObjectsRef UI;

	private const int ProtectionBarren = 825765986;

	private const int ProtectionLoss = 912668723;

	internal static readonly HashSet<int> UnavailableTypes = new HashSet<int>();

	// ───────────────────── 危险状态：警告 + 二次确认 ─────────────────────
	// 这些状态不是不能用，而是「开之前得想清楚」。所以统一做成：
	//   状态条上把名字染黄 → 点第一次只弹警告、不生效 → 名字染红 → 再点一次才真正挂上。
	// 关闭不受影响，照常直接关。
	//
	// 危险的出处分两类：
	//   ① 写进存档会让读档崩溃 —— v1.0.18 在真机上把 70 个状态全扫了一遍，
	//      带 SlipDamage / AutoDead / GameEnd 扩展的只有下面这 4 个。
	//      （早先按名字猜的那份 10 条名单实测 8 条误报、2 条漏报，已弃用。）
	//      读档时游戏会走 AbnormalSetup -> AddAbnormal(data, level, damageStack)，
	//      而存档里只存了「类型 + 等级」、没有伤害栈，这类状态会在这条路上空引用崩溃，
	//      异常打断整条异步链 —— 现象就是黑屏且按键全部失灵。
	//   ② 游戏 API 设置它会把玩家输入卡死，只能重启游戏 —— 早期实测。

	/// <summary>危险状态名单：typeId -> 给玩家看的危险原因。</summary>
	private static readonly System.Collections.Generic.Dictionary<int, string> RiskyStatuses = new System.Collections.Generic.Dictionary<int, string>
	{
		{ 895562034, "写进存档后读档会崩溃" },
		{ 858940217, "写进存档后读档会崩溃" },
		{ 926245473, "写进存档后读档会卡住" },
		{ 858927927, "写进存档后读档会卡住" },
		{ 1650800693, "设置后可能卡住输入，只能重启游戏" },
		{ 1630627383, "设置后可能卡住输入，只能重启游戏" },
		{ 912668513, "设置后可能卡住输入，只能重启游戏" }
	};

	/// <summary>运行时判定结果缓存：typeId -> 风险原因（存 null 表示「已判定为安全」）。</summary>
	private static readonly System.Collections.Generic.Dictionary<int, string> SaveRisks = new System.Collections.Generic.Dictionary<int, string>();

	/// <summary>本次游戏里已经提醒过的状态，避免同一个状态反复刷提示。</summary>
	private static readonly System.Collections.Generic.HashSet<int> RiskWarned = new System.Collections.Generic.HashSet<int>();

	/// <summary>正等着第二次确认的危险状态；0 表示当前没有。</summary>
	private static int _pendingConfirmType;

	/// <summary>「已挂状态里有高危项」这项检查的节流时间。</summary>
	private static float _riskNoticeCooldown;

	private static bool _loggedBlockedClick;

	private static string _lastTickError;

	private static float _lastTickErrorTime;

	private static float _tickCooldown;

	private static float _lastRefresh;

	/// <summary>
	/// 全部 70 个异常状态的类型 ID，顺序与游戏的 AbnormalType 枚举声明顺序一致。
	///
	/// 这张表是全模组的数据源头：RebuildActiveSet 靠它逐个数一遍「现在挂了哪些状态」，
	/// 面板的 70 行也照它来建。取值方式是用反编译工具读游戏程序集里的
	/// AbnormalType 枚举，再逐个配中文名（见 TypeNames.Table）。
	///
	/// ⚠ 游戏以后新增异常状态时，这里**不会**报错，只会静默少一两个 ——
	/// 升级游戏后需要重新对一遍枚举，把新 ID 补进来，同时把下面的 [70] 改掉。
	/// </summary>
	internal static readonly int[] AllTypeValues = new int[70]
	{
		825648481, 1681275188, 1684355129, 912668723, 926496313, 1630627383, 1647458103, 926037814, 1701143864, 1717855074,
		909664822, 892745521, 895562034, 960050227, 1630561332, 959538532, 1667393074, 926036019, 1650681443, 926245473,
		812003637, 875902512, 909140581, 946092089, 862086243, 1650811237, 912668513, 926180450, 960049207, 825765986,
		895628336, 1697985080, 959930726, 876032055, 909522533, 959853109, 875574114, 1650603577, 875651684, 875968056,
		842555749, 892429874, 811819831, 1650800693, 926038369, 892483896, 828585015, 909468724, 1681340722, 926363747,
		943206711, 1630613811, 892483891, 1647457636, 858940217, 959721780, 1647863137, 862270521, 808858210, 1664234806,
		842283320, 1698181682, 892940601, 825373751, 926180454, 862074214, 858927927, 962880824, 862335028, 1664246113
	};

	private static bool _signatureLoggedLoad;

	private static readonly HashSet<int> _loadRequested = new HashSet<int>();

	private static readonly HashSet<int> _activeTypes = new HashSet<int>();

	private static int _lastActiveCount = -1;

	private static bool _activeSetDirty;

	private static readonly System.Collections.Generic.Dictionary<int, int> _loadAttempts = new System.Collections.Generic.Dictionary<int, int>();

	private static readonly System.Collections.Generic.Dictionary<int, float> _loadRequestTime = new System.Collections.Generic.Dictionary<int, float>();

	private const int MaxAutoLoadAttempts = 3;

	private const float LoadWaitTimeout = 8f;

	private const int MaxNameScanLevel = 20;

	private static bool _apiChecked;

	private static bool _apiGalleryChecked;

	private static float _buildCooldownUntil;

	private static int _buildFailCount;

	internal static bool PanelVisible;

	internal static int OpenedFrame = -1;

	internal static int ClosedFrame = -1;

	private static bool _suppressAutoShow;

	private static float _hintWarnUntil;

	private const float HintWarnSeconds = 6f;

	private static GalleryConditionUI _condCached;

	private static float _condSearchCooldown;

	private static readonly System.Collections.Generic.List<CanvasGroup> _condGroups = new System.Collections.Generic.List<CanvasGroup>();

	private static GalleryConditionUI _condGroupsOwner;

	private static int _selectedIndex;

	private static bool _hasSelectedUi;

	private static readonly Color StateOnColor = new Color(0.45f, 1f, 0.6f, 1f);

	private static readonly Color StateOffColor = new Color(1f, 1f, 1f, 0.5f);

	private static readonly Color StateUnavailableColor = new Color(1f, 0.55f, 0.55f, 0.7f);

	private static readonly Color StateLoadingColor = new Color(0.75f, 0.78f, 0.85f, 0.65f);

	private static readonly Color NameNormalColor = new Color(1f, 1f, 1f, 0.95f);

	private static readonly Color NameBlockedColor = new Color(0.72f, 0.72f, 0.76f, 0.75f);

	/// <summary>危险状态的名字颜色（黄）：提醒玩家点它之前先存档。</summary>
	private static readonly Color NameRiskColor = new Color(1f, 0.84f, 0.36f, 1f);

	/// <summary>危险状态已经点过一次、正等第二次确认时的名字颜色（红）。</summary>
	private static readonly Color NameArmedColor = new Color(1f, 0.42f, 0.34f, 1f);

	private static readonly Color BgSelectedColor = new Color(0.55f, 0.45f, 0.25f, 0.97f);

	private static readonly Color BgOnColor = new Color(0.16f, 0.24f, 0.18f, 0.95f);

	private static readonly Color BgOffColor = new Color(0.13f, 0.16f, 0.22f, 0.92f);

	private static readonly string[] LvText = BuildLvTextTable(20);

	private const int CodeBlocked = -2;

	private const int CodeLoading = -1;

	private const int CodeFailed = -3;

	/// <summary>危险状态、还没开：状态栏显示 ! 而不是 OFF。</summary>
	private const int CodeRisky = -4;

	/// <summary>危险状态、已经点过一次等确认：状态栏显示 !! 并转红。</summary>
	private const int CodeRiskyArmed = -5;

	private static readonly HashSet<int> _rowErrLogged = new HashSet<int>();

	internal static readonly System.Collections.Generic.Dictionary<int, string> NameCache = new System.Collections.Generic.Dictionary<int, string>();

	private static float _lazyLoadCooldown;

	private static Keyboard _kbCache;

	private static float _kbRetryTime;

	/// <summary>模组版本号（Major.Minor.Build），取自程序集版本；读取失败时返回 “?”。</summary>
	internal static string ModVersion
	{
		get
		{
			if (_modVersion == null)
			{
				try
				{
					Version version = Assembly.GetExecutingAssembly().GetName().Version;
					_modVersion = version.Major + "." + version.Minor + "." + version.Build;
				}
				catch
				{
					_modVersion = "?";
				}
			}
			return _modVersion;
		}
	}

	/// <summary>屏蔽列表文件路径（Mods/AllAbnormalMod_blocked.txt）；取路径失败时返回 null。</summary>
	private static string BlockedFilePath
	{
		get
		{
			try
			{
				return Path.Combine(Environment.CurrentDirectory, "Mods", "AllAbnormalMod_blocked.txt");
			}
			catch
			{
				return null;
			}
		}
	}

	/// <summary>模组初始化：载入屏蔽列表、登记危险类型、初始化输入模块，并做签名自检。</summary>
	public override void OnInitializeMelon()
	{
		try
		{
			LoadBlockedList();
			// 危险状态改走「警告 + 二次确认」，不再当作不可用状态屏蔽。
			// 这里主动把它们从列表里剔除：旧版本的自己可能已经把它们写进 blocked.txt 了。
			foreach (int riskyType in RiskyStatuses.Keys)
			{
				UnavailableTypes.Remove(riskyType);
			}
			PanelInput.InitIl2Cpp();
			base.LoggerInstance.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Initialized. [Signature] 大赢经直插白皮赢道&汐蓝 [DYJ-CBWP-YD-XL]");
			// 签名自检：签名常量被第三方改动时给出告警。
			if (Signature.Assembled != "大赢经直插白皮赢道&汐蓝")
			{
				MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] signature self-check mismatch");
			}
		}
		catch (Exception ex)
		{
			MelonLogger.Error("[AllAbnormalMod][DYJ-CBWP-YD-XL] Init failed: " + ex.Message);
		}
	}

	/// <summary>从 Mods/AllAbnormalMod_blocked.txt 载入被屏蔽的状态类型（每行一个十进制 ID，# 之后为注释）。</summary>
	private static void LoadBlockedList()
	{
		try
		{
			string blockedFilePath = BlockedFilePath;
			if (blockedFilePath == null || !File.Exists(blockedFilePath))
			{
				return;
			}
			string[] lines = File.ReadAllLines(blockedFilePath);
			for (int i = 0; i < lines.Length; i++)
			{
				string lineText = lines[i].Trim();
				int commentIndex = lineText.IndexOf('#');
				if (commentIndex >= 0)
				{
					lineText = lineText.Substring(0, commentIndex).Trim();
				}
				if (lineText.Length != 0 && int.TryParse(lineText, out var typeId))
				{
					UnavailableTypes.Add(typeId);
				}
			}
			if (UnavailableTypes.Count > 0)
			{
				MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Loaded blocked status list: " + UnavailableTypes.Count + " entries.");
			}
		}
		// 屏蔽文件缺失或损坏时静默跳过，不能影响模组启动。
		catch
		{
		}
	}

	/// <summary>把无法通过游戏 API 设置的状态类型追加写入屏蔽文件。</summary>
	private static void SaveBlocked(int type)
	{
		try
		{
			string blockedFilePath = BlockedFilePath;
			if (blockedFilePath != null)
			{
				string directoryName = Path.GetDirectoryName(blockedFilePath);
				if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				if (!File.Exists(blockedFilePath))
				{
					File.AppendAllText(blockedFilePath, "# AllAbnormalMod by 大赢经直插白皮赢道&汐蓝 [DYJ-CBWP-YD-XL]" + Environment.NewLine);
					File.AppendAllText(blockedFilePath, "# 每行一个被屏蔽的状态类型 ID；删掉对应行即可重新启用。" + Environment.NewLine);
				}
				File.AppendAllText(blockedFilePath, type + "   # 已自动屏蔽（游戏接口无法设置该状态）[DYJ-CBWP-YD-XL]" + Environment.NewLine);
			}
		}
		// 写屏蔽文件失败（磁盘只读等）不应打断游戏流程。
		catch
		{
		}
	}

	/// <summary>把某一行标记为不可用；首次发现该类型时写入屏蔽文件并打印警告。</summary>
	private static void MarkUnavailable(StatusRow row)
	{
		if (row == null)
		{
			return;
		}
		int type = (int)row.Type;
		// 危险状态不做永久屏蔽：它们的定位是「能开，但得先看警告、再点第二下确认」。
		// 写进 blocked.txt 等于剥夺玩家再试一次的机会，而且下次启动还会读回来。
		if (RiskyStatuses.ContainsKey(type))
		{
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] risky status " + type + " could not be set this time; kept clickable (not blocked).");
			return;
		}
		row.Blocked = true;
		if (UnavailableTypes.Add(type))
		{
			SaveBlocked(type);
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] Status type " + type + " cannot be set through the game API; blocked (shown as x). Edit Mods/AllAbnormalMod_blocked.txt to unblock.");
		}
	}

	/// <summary>MelonLoader 每帧回调：带冷却地调用 Tick()，并把反复出现的异常降频打印。</summary>
	public override void OnUpdate()
	{
		if (Time.time < _tickCooldown)
		{
			return;
		}
		try
		{
			Tick();
		}
		catch (Exception ex)
		{
			// 出错后冷却 2 秒，避免每帧抛异常导致日志刷屏。
			_tickCooldown = Time.time + 2f;
			string message = ex.Message;
			// 同一条错误信息 15 秒内只打印一次。
			if (message != _lastTickError || Time.time - _lastTickErrorTime > 15f)
			{
				_lastTickError = message;
				_lastTickErrorTime = Time.time;
				MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] Tick skipped (manager access restricted): " + message);
			}
		}
	}

	/// <summary>退出时释放所有行引用、名称缓存与 UI 引用，避免 IL2CPP 对象在卸载后被持有。</summary>
	public override void OnApplicationQuit()
	{
		try
		{
			MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] shutdown, releasing " + Rows.Count + " row refs. 大赢经直插白皮赢道&汐蓝");
		}
		catch
		{
		}
		try
		{
			for (int i = 0; i < Rows.Count; i++)
			{
				StatusRow statusRow = Rows[i];
				if (statusRow != null)
				{
					statusRow.Data = null;
					statusRow.NameText = null;
					statusRow.StateText = null;
					statusRow.Bg = null;
				}
			}
			Rows.Clear();
			NameCache.Clear();
			UI = null;
		}
		catch
		{
		}
	}

	/// <summary>场景切换后重置按需加载缓存，让所有行重新拉取数据。</summary>
	public override void OnSceneWasLoaded(int buildIndex, string sceneName)
	{
		try
		{
			ResetLoadCache();
			MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] scene loaded (" + sceneName + ") -> on-demand cache reset, " + Rows.Count + " rows will reload. [大赢经直插白皮赢道&汐蓝]");
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] ResetLoadCache failed: " + ex.Message);
		}
	}

	/// <summary>重置按需加载状态、清空行的视觉缓存，并把 UI 标记为需要重建行。</summary>
	private static void ResetLoadCache()
	{
		_loadRequested.Clear();
		_loadAttempts.Clear();
		_loadRequestTime.Clear();
		_lazyLoadCooldown = 0f;
		_activeTypes.Clear();
		_activeSetDirty = true;
		// 换了场景就把「等待二次确认」也撤掉：跨场景还留着的话，
		// 玩家回来随手一点就直接开启了，等于二次确认被绕过。
		_pendingConfirmType = 0;
		for (int i = 0; i < Rows.Count; i++)
		{
			StatusRow statusRow = Rows[i];
			if (statusRow != null)
			{
				statusRow.Data = null;
				statusRow.LastLv = -1;
				statusRow.LastStateCode = int.MinValue;
				statusRow.ModelLv = -1;
				statusRow.LastBgLv = -1;
				statusRow.LastSelected = false;
				statusRow.LastNameBlocked = false;
				statusRow.LastName = null;
			}
		}
		if (UI != null)
		{
			UI.RowsDirty = true;
		}
	}

	/// <summary>请求游戏按需加载某个异常类型的数据；已经请求过则直接返回 true。</summary>
	internal static bool EnsureTypeLoaded(AbnormalType type)
	{
		int typeId = (int)type;
		if (_loadRequested.Contains(typeId))
		{
			return true;
		}
		_loadRequested.Add(typeId);
		_loadRequestTime[typeId] = Time.time;
		if (!_signatureLoggedLoad)
		{
			_signatureLoggedLoad = true;
			MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] on-demand loader active, first type " + typeId + " [大赢经直插白皮赢道&汐蓝]");
		}
		try
		{
			AbnormalManager abnormal = ManagerList.Abnormal;
			if (abnormal == null)
			{
				_loadRequested.Remove(typeId);
				_loadRequestTime.Remove(typeId);
				return false;
			}
			CancellationToken token = new CancellationToken();
			abnormal.LoadAbnormalData(type, token);
			return true;
		}
		catch
		{
			_loadRequested.Remove(typeId);
			_loadRequestTime.Remove(typeId);
			return false;
		}
	}

	/// <summary>读取指定类型的原始异常数据；过程中任何异常都当作拿不到数据处理。</summary>
	internal static AbnormalData TryGetData(AbnormalType type)
	{
		try
		{
			AbnormalManager abnormal = ManagerList.Abnormal;
			if (abnormal == null)
			{
				return null;
			}
			Il2CppSystem.Collections.Generic.Dictionary<AbnormalType, AbnormalOriginalWrapper> abnormalDict = abnormal.AbnormalDict;
			if (abnormalDict == null)
			{
				return null;
			}
			AbnormalOriginalWrapper wrapper = null;
			if (!abnormalDict.TryGetValue(type, out wrapper))
			{
				return null;
			}
			return wrapper?.OriginalData;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>用户主动关闭面板：同时抑制自动弹出，避免同一帧又被自动打开。</summary>
	internal static void ClosePanelByUser()
	{
		PanelVisible = false;
		_suppressAutoShow = true;
		// 记录关闭帧，避免同一帧内又被 Home/Delete 立刻打开。
		ClosedFrame = Time.frameCount;
		MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Panel closed by Home/Delete/Backspace.");
	}

	/// <summary>取得图鉴条件 UI（GalleryConditionUI）：优先用缓存，0.5 秒内只尝试搜索一次。</summary>
	private static GalleryConditionUI GetCond()
	{
		try
		{
			if (_condCached != null && _condCached.gameObject != null)
			{
				return _condCached;
			}
		}
		catch
		{
		}
		_condCached = null;
		// 0.5 秒内不重复搜索图鉴 UI，避免每帧遍历对象树。
		if (Time.time < _condSearchCooldown)
		{
			return null;
		}
		_condSearchCooldown = Time.time + 0.5f;
		try
		{
			GalleryManager gallery = ManagerList.Gallery;
			if (gallery == null)
			{
				return null;
			}
			GalleryUI galleryUI = gallery.GalleryUI;
			if (galleryUI == null)
			{
				return null;
			}
			_condCached = galleryUI.GalleryConditionUI;
			if (_condCached != null && !_apiGalleryChecked)
			{
				_apiGalleryChecked = true;
				MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] API check OK: Gallery UI chain available.");
			}
			return _condCached;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>取得并缓存当前键盘设备；设备失效时清空缓存，1 秒内不重复尝试获取。</summary>
	private static Keyboard GetCachedKeyboard()
	{
		if (_kbCache != null)
		{
			try
			{
				// 设备已销毁时 added 不可用，需要重新获取键盘对象。
				if (_kbCache.added)
				{
					return _kbCache;
				}
			}
			catch
			{
			}
			_kbCache = null;
		}
		if (Time.time < _kbRetryTime)
		{
			return null;
		}
		_kbRetryTime = Time.time + 1f;
		try
		{
			_kbCache = Keyboard.current;
		}
		catch
		{
		}
		return _kbCache;
	}

	/// <summary>面板主循环：负责面板重建、显示与隐藏、按需加载，以及行的节流刷新。</summary>
	private static void Tick()
	{
		if (_hintWarnUntil > 0f && Time.time >= _hintWarnUntil)
		{
			RestoreHint();
		}
		try
		{
			// 每帧最多构建 6 行，避免一次性生成大量 UI 造成卡顿。
			PanelBuilder.StepPendingRows(6);
		}
		catch
		{
		}
		if (!_apiChecked)
		{
			try
			{
				if (ManagerList.PlayerStatus != null && ManagerList.PlayerStatus.AbnormalList != null)
				{
					_apiChecked = true;
					MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] API check OK: PlayerStatus / AbnormalList available.");
				}
			}
			catch
			{
			}
		}
		// 玩家在游戏设置里改了分辨率 -> 现有面板的尺寸是按旧分辨率算好的，必须销毁重建。
		// 否则从高分辨率切到低分辨率时面板会顶出屏幕：状态名左边被裁掉、列表只露几行。
		if (UI != null && UI.Root != null && PanelBuilder.WasBuiltForDifferentScreen())
		{
			MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Screen changed to " + Screen.width + "x" + Screen.height
				+ " (panel was built for " + PanelBuilder.BuiltScreenW + "x" + PanelBuilder.BuiltScreenH + "), rebuilding panel.");
			try
			{
				UnityEngine.Object.Destroy(UI.Root);
			}
			catch
			{
			}
			UI = null;
			PanelBuilder.ClearPending();
			Rows.Clear();
			_buildCooldownUntil = 0f;
			_buildFailCount = 0;
		}
		if (UI == null || UI.Root == null)
		{
			if (Time.time >= _buildCooldownUntil)
			{
				try
				{
					PanelBuilder.DestroyStrayPanels();
				}
				catch
				{
				}
				if (!BuildUI())
				{
					_buildFailCount++;
					_buildCooldownUntil = Time.time + ((_buildFailCount >= 5) ? 30f : 2f);
					if (_buildFailCount == 1 || _buildFailCount % 10 == 0)
					{
						MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] Panel build failed (x" + _buildFailCount + ", reason: " + (string.IsNullOrEmpty(PanelBuilder.LastError) ? "unknown" : PanelBuilder.LastError) + ")");
					}
				}
				else if (Rows.Count > 0)
				{
					_buildFailCount = 0;
				}
			}
			if (UI == null || UI.Root == null)
			{
				return;
			}
		}
		if (Rows.Count == 0 && !PanelBuilder.HasPendingRows)
		{
			if (!(Time.time < _buildCooldownUntil))
			{
				_buildFailCount++;
				_buildCooldownUntil = Time.time + ((_buildFailCount >= 5) ? 30f : 2f);
				if (_buildFailCount == 1 || _buildFailCount % 10 == 0)
				{
					MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] panel has 0 rows built (build aborted?), rebuilding (x" + _buildFailCount + ").");
				}
				try
				{
					UnityEngine.Object.Destroy(UI.Root);
				}
				catch
				{
				}
				UI = null;
				PanelBuilder.ClearPending();
			}
			return;
		}
		bool galleryOpen = false;
		try
		{
			GalleryConditionUI cond = GetCond();
			if (cond != null && IsCondVisible(cond))
			{
				galleryOpen = true;
			}
		}
		catch
		{
		}
		if (!galleryOpen)
		{
			_suppressAutoShow = false;
		}
		bool shouldShow = PanelVisible || (galleryOpen && !_suppressAutoShow);
		if (!shouldShow)
		{
			if (UI.Root.activeSelf)
			{
				UI.HideAuto();
			}
			try
			{
				Keyboard cachedKeyboard = GetCachedKeyboard();
				// 同一帧刚关闭过就不再响应开关，防止抖一下又被打开。
				if (cachedKeyboard != null && ClosedFrame != Time.frameCount && (cachedKeyboard.homeKey.wasPressedThisFrame || cachedKeyboard.deleteKey.wasPressedThisFrame))
				{
					PanelVisible = true;
					_suppressAutoShow = false;
					OpenedFrame = Time.frameCount;
					shouldShow = true;
					MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Panel opened by Home/Delete.");
				}
			}
			catch
			{
			}
		}
		if (!shouldShow)
		{
			if (UI.Root.activeSelf)
			{
				UI.HideAuto();
			}
			return;
		}
		if (!UI.Root.activeSelf)
		{
			UI.Show();
			RebuildActiveSet();
			RefreshActiveCount(force: true);
			_loadAttempts.Clear();
			// 面板刚打开：上一次留下的「待二次确认」作废。
			// 否则关掉面板隔一阵再打开，那个状态名字还是红的，点一下就直接开了。
			_pendingConfirmType = 0;
		}
		try
		{
			StepLazyLoad();
		}
		catch
		{
		}
		// 面板开着时定期检查「当前挂着的高危状态」，作为第二道提醒。
		try
		{
			StepSaveRiskNotice();
		}
		catch
		{
		}
		float time = Time.time;
		// 游戏侧数据发生变化时才重建激活集合。
		if (_activeSetDirty && GetAbnormalList() != null)
		{
			_activeSetDirty = false;
			RebuildActiveSet();
			RefreshActiveCount(force: true);
		}
		// 行刷新节流：有变化时最快 0.05 秒一次，另外 0.5 秒兜底刷新一次。
		if ((UI.RowsDirty && time - _lastRefresh >= 0.05f) || time - _lastRefresh > 0.5f)
		{
			UI.RowsDirty = false;
			_lastRefresh = time;
			RefreshRows();
			RefreshActiveCount(force: false);
		}
	}

	/// <summary>判断图鉴条件 UI 是否真的可见：沿父级链收集 CanvasGroup，任一 alpha 小于 0.01 即视为隐藏。</summary>
	private static bool IsCondVisible(GalleryConditionUI cond)
	{
		if (cond == null)
		{
			return false;
		}
		try
		{
			GameObject condObject = cond.gameObject;
			if (condObject == null || !condObject.activeInHierarchy)
			{
				return false;
			}
		}
		catch
		{
			return false;
		}
		// 只有换成另一个条件 UI 时才重新收集父级 CanvasGroup 链。
		if (_condGroupsOwner != cond)
		{
			_condGroupsOwner = cond;
			_condGroups.Clear();
			try
			{
				Transform node = cond.GetComponent<RectTransform>();
				int depth = 0;
				while (node != null && depth < 32)
				{
					try
					{
						CanvasGroup parentGroup = node.GetComponent<CanvasGroup>();
						if (parentGroup != null)
						{
							_condGroups.Add(parentGroup);
						}
					}
					catch
					{
					}
					node = node.parent;
					depth++;
				}
			}
			catch
			{
			}
		}
		for (int i = 0; i < _condGroups.Count; i++)
		{
			CanvasGroup canvasGroup = _condGroups[i];
			if (canvasGroup == null)
			{
				continue;
			}
			try
			{
				// 父级链上任一 CanvasGroup 淡出，即认为图鉴界面没有打开。
				if (canvasGroup.alpha < 0.01f)
				{
					return false;
				}
			}
			catch
			{
			}
		}
		return true;
	}

	/// <summary>调用 PanelBuilder 构建面板并接管返回值；构建异常只记日志，不向外抛出。</summary>
	private static bool BuildUI()
	{
		try
		{
			GameObjectsRef builtUi = PanelBuilder.Build();
			if (builtUi == null || builtUi.Root == null)
			{
				return false;
			}
			UI = builtUi;
			// 置为 -1 以强制下一次刷新计数文本。
			_lastActiveCount = -1;
			MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Mod panel ready.");
			return true;
		}
		catch (Exception ex)
		{
			MelonLogger.Error("[AllAbnormalMod][DYJ-CBWP-YD-XL] Panel build exception: " + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// 设置当前选中的行。
	///
	/// 选中高亮是画在行背景色上的，而背景由 RefreshRows 按可视区批量写，
	/// 所以这里直接刷一遍可视区（而不是只刷变化的两行）；
	/// 再单独补刷「刚刚失去选中」的那一行 —— 它可能已经滚出可视区，
	/// 不在上面那轮的范围内，不补的话会留在高亮状态。
	/// </summary>
	internal static void SetSelected(int index)
	{
		int previousIndex = _selectedIndex;
		_selectedIndex = index;
		_hasSelectedUi = true;
		RefreshRows();
		if (previousIndex != index)
		{
			RefreshRowAt(previousIndex);
		}
	}

	/// <summary>预生成 “Lv0” 到 “Lvmax” 的文本表，避免每帧拼接字符串。</summary>
	private static string[] BuildLvTextTable(int max)
	{
		string[] table = new string[max + 1];
		for (int i = 0; i <= max; i++)
		{
			table[i] = "Lv" + i;
		}
		return table;
	}

	/// <summary>刷新单行：等级、状态文本、状态名与背景色，只在值发生变化时才写 UI。</summary>
	private static void RefreshRow(int index, AbnormalList list, bool probeData)
	{
		if (index < 0 || index >= Rows.Count)
		{
			return;
		}
		StatusRow statusRow = Rows[index];
		if (statusRow == null)
		{
			return;
		}
		try
		{
			// 一旦被判定为不可用就永久屏蔽（游戏 API 无法设置该状态）。
			bool isBlocked = (statusRow.Blocked = statusRow.Blocked || UnavailableTypes.Contains((int)statusRow.Type));
			// 危险状态：名字要染警示色，其中「已点过一次等确认」的那个还要再红一档。
			bool isRisky = RiskyStatuses.ContainsKey((int)statusRow.Type);
			bool isArmed = isRisky && _pendingConfirmType == (int)statusRow.Type;
			if (statusRow.Data == null && !isBlocked && probeData)
			{
				statusRow.Data = TryGetData(statusRow.Type);
				if (statusRow.Data != null)
				{
					NameCache.Remove((int)statusRow.Type);
					CacheRowName(statusRow);
					statusRow.LastName = null;
					// 数据一到手就顺便判定它的存档风险，把结果记进日志备查。
					JudgeSaveRisk((int)statusRow.Type, statusRow.Data);
				}
			}
			int currentLevel = 0;
			if (statusRow.Data != null)
			{
				if (list != null)
				{
					try
					{
						currentLevel = (statusRow.ModelLv = list.GetAbnormalLevel(statusRow.Type));
						if (currentLevel > 0)
						{
							_activeTypes.Add((int)statusRow.Type);
						}
						else
						{
							_activeTypes.Remove((int)statusRow.Type);
						}
					}
					catch
					{
						currentLevel = ((statusRow.ModelLv >= 0) ? statusRow.ModelLv : 0);
					}
				}
				else
				{
					currentLevel = ((statusRow.ModelLv >= 0) ? statusRow.ModelLv : 0);
				}
			}
			int loadAttempts;
			// 状态码约定：-5 危险待确认 / -4 危险未开 / -3 加载失败 / -2 被屏蔽 /
			//             -1 加载中 / 大于等于 0 表示当前等级。
			int stateCode;
			if (isBlocked)
			{
				stateCode = CodeBlocked;
			}
			else if (statusRow.Data == null)
			{
				stateCode = ((_loadAttempts.TryGetValue((int)statusRow.Type, out loadAttempts) && loadAttempts >= MaxAutoLoadAttempts) ? CodeFailed : CodeLoading);
			}
			else if (isRisky && currentLevel <= 0)
			{
				// 危险状态还没开：状态栏直接显示警示符，比显示 OFF 有用得多。
				stateCode = (isArmed ? CodeRiskyArmed : CodeRisky);
			}
			else
			{
				stateCode = currentLevel;
			}
			if (statusRow.StateText != null && statusRow.LastStateCode != stateCode)
			{
				statusRow.LastStateCode = stateCode;
				string stateLabel;
				Color stateColor;
				if (isBlocked)
				{
					stateLabel = "×";
					stateColor = StateUnavailableColor;
				}
				else
				{
					switch (stateCode)
					{
					case CodeFailed:
						stateLabel = "✗";
						stateColor = StateUnavailableColor;
						break;
					case CodeRisky:
						// 危险状态、还没开：一个感叹号，配上黄色的名字就够醒目了。
						stateLabel = "!";
						stateColor = NameRiskColor;
						break;
					case CodeRiskyArmed:
						// 已经点过一次：两个感叹号 + 红色，意思是「再点一次就真开了」。
						stateLabel = "!!";
						stateColor = NameArmedColor;
						break;
					case CodeLoading:
						stateLabel = "…";
						stateColor = StateLoadingColor;
						break;
					default:
						if (currentLevel <= 0)
						{
							stateLabel = "OFF";
							stateColor = StateOffColor;
						}
						else
						{
							stateLabel = ((currentLevel < LvText.Length) ? LvText[currentLevel] : ("Lv" + currentLevel));
							stateColor = StateOnColor;
						}
						break;
					}
				}
				statusRow.StateText.text = stateLabel;
				statusRow.StateText.color = stateColor;
			}
			// 等级、名称、屏蔽状态或危险状态有变化时才写 UI，减少文本与颜色赋值开销。
			if (currentLevel != statusRow.LastLv || statusRow.LastName == null || statusRow.LastNameBlocked != isBlocked || statusRow.LastRisky != isRisky || statusRow.LastArmed != isArmed)
			{
				statusRow.LastLv = currentLevel;
				if (statusRow.NameText != null)
				{
					string statusName = GetStatusName(statusRow, currentLevel);
					if (statusRow.LastName != statusName || statusRow.LastNameBlocked != isBlocked || statusRow.LastRisky != isRisky || statusRow.LastArmed != isArmed)
					{
						statusRow.LastName = statusName;
						statusRow.LastNameBlocked = isBlocked;
						statusRow.LastRisky = isRisky;
						statusRow.LastArmed = isArmed;
						statusRow.NameText.text = statusName;
						// 危险状态的名字染警示色：黄=有风险，红=已点过一次、再点就生效。
						if (isBlocked)
						{
							statusRow.NameText.color = NameBlockedColor;
						}
						else if (isArmed)
						{
							statusRow.NameText.color = NameArmedColor;
						}
						else if (isRisky)
						{
							statusRow.NameText.color = NameRiskColor;
						}
						else
						{
							statusRow.NameText.color = NameNormalColor;
						}
					}
				}
			}
			// 选中高亮：只有被选中的那一行使用选中底色。
			bool isSelected = _hasSelectedUi && index == _selectedIndex;
			if (statusRow.Bg != null && (statusRow.LastSelected != isSelected || statusRow.LastBgLv != currentLevel))
			{
				statusRow.LastSelected = isSelected;
				statusRow.LastBgLv = currentLevel;
				statusRow.Bg.color = (isSelected ? BgSelectedColor : ((currentLevel > 0) ? BgOnColor : BgOffColor));
			}
		}
		catch (Exception ex)
		{
			LogRowError(index, ex);
		}
	}

	/// <summary>记录行刷新异常；同一下标只记一次，最多打印 5 条。</summary>
	private static void LogRowError(int index, Exception ex)
	{
		if (_rowErrLogged.Add(index) && _rowErrLogged.Count <= 5)
		{
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] row " + index + " refresh failed: " + ex.Message);
		}
	}

	/// <summary>刷新指定下标的行（键盘或鼠标操作后调用）。</summary>
	internal static void RefreshRowAt(int index)
	{
		if (index >= 0 && index < Rows.Count)
		{
			RefreshRow(index, GetAbnormalList(), probeData: true);
		}
	}

	/// <summary>只做行的视觉初始化：不探测数据、不请求加载。</summary>
	internal static void InitRowVisual(int index)
	{
		RefreshRow(index, null, probeData: false);
	}

	/// <summary>刷新当前滚动可视范围内的所有行，屏幕外的行跳过以省性能。</summary>
	private static void RefreshRows()
	{
		if (Rows.Count == 0)
		{
			return;
		}
		AbnormalList abnormalList = GetAbnormalList();
		bool probeData = abnormalList != null;
		int firstIndex = 0;
		int lastIndex = Rows.Count - 1;
		if (UI != null)
		{
			float rowStride = UI.RowH + UI.RowSpacing;
			float viewHeight = UI.ViewHeight;
			// 只刷新可视区上下各多一行的范围，屏幕外的行不动。
			if (rowStride > 0.01f && viewHeight > 1f)
			{
				firstIndex = Mathf.Max(0, Mathf.FloorToInt(UI.ScrollY / rowStride) - 1);
				lastIndex = Mathf.Min(Rows.Count - 1, Mathf.CeilToInt((UI.ScrollY + viewHeight) / rowStride) + 1);
			}
		}
		if (firstIndex > lastIndex)
		{
			firstIndex = lastIndex;
		}
		for (int i = firstIndex; i <= lastIndex; i++)
		{
			RefreshRow(i, abnormalList, probeData);
		}
	}

	/// <summary>解析某等级对应的本地化名称 ID：优先取该等级的名字，取不到则退回整体名称 ID。</summary>
	private static int ResolveNameId(StatusRow row, int level)
	{
		if (level >= 1)
		{
			try
			{
				AbnormalOne abnormalOne = row.Data.GetAbnormalOne(level);
				if (abnormalOne != null)
				{
					return (int)abnormalOne.m_AbnormalNameID;
				}
			}
			catch
			{
			}
		}
		try
		{
			return (int)row.Data.AbnormalNameID;
		}
		catch
		{
		}
		return 0;
	}

	/// <summary>判断一个名字能否作为显示名：非空、不超过 14 字、不含富文本标记与换行。</summary>
	private static bool IsUsableName(string nameText)
	{
		if (string.IsNullOrEmpty(nameText))
		{
			return false;
		}
		if (nameText.Length > 14)
		{
			return false;
		}
		// 名字里出现 "[[" 是富文本标记，不能直接当显示名。
		if (nameText.IndexOf("[[", StringComparison.Ordinal) >= 0)
		{
			return false;
		}
		if (nameText.IndexOf('\n') >= 0)
		{
			return false;
		}
		return true;
	}

	/// <summary>把名称 ID 解析成文本：先查游戏本地化，再退回内置中文名表。</summary>
	private static string ResolveNameText(int nameId)
	{
		if (nameId <= 0)
		{
			return null;
		}
		string localized = null;
		try
		{
			LocalizeManager localize = ManagerList.Localize;
			if (localize != null)
			{
				localized = localize.GetLcText((LocalizeID)nameId);
			}
		}
		catch
		{
		}
		if (!string.IsNullOrEmpty(localized))
		{
			return localized;
		}
		if (ZhNames.Table.TryGetValue(nameId, out var zhName) && IsUsableName(zhName))
		{
			return zhName;
		}
		return null;
	}

	/// <summary>为某一行建立显示名缓存：取能拿到的第一个等级名，兜底使用类型名表。</summary>
	internal static void CacheRowName(StatusRow row)
	{
		if (row == null || row.Data == null)
		{
			return;
		}
		int type = (int)row.Type;
		if (NameCache.ContainsKey(type))
		{
			return;
		}
		string cachedName = null;
		int maxLevel = 1;
		try
		{
			// 等级上限封顶 20，与 LvText 表的长度保持一致。
			maxLevel = Mathf.Clamp(row.Data.MaxLevel, 1, MaxNameScanLevel);
		}
		catch
		{
		}
		for (int i = 1; i <= maxLevel; i++)
		{
			string resolved = ResolveNameText(ResolveNameId(row, i));
			if (!string.IsNullOrEmpty(resolved))
			{
				cachedName = resolved;
				break;
			}
		}
		if (string.IsNullOrEmpty(cachedName))
		{
			cachedName = ResolveNameText(ResolveNameId(row, 0));
		}
		if (string.IsNullOrEmpty(cachedName))
		{
			cachedName = (TypeNames.Table.TryGetValue(type, out var fallbackName) ? fallbackName : null);
		}
		if (!string.IsNullOrEmpty(cachedName))
		{
			NameCache[type] = cachedName;
		}
	}

	/// <summary>取得某行在指定等级下应显示的名称，逐级降级到缓存、名称表与 “#类型ID”。</summary>
	internal static string GetStatusName(StatusRow row, int level)
	{
		if (row == null)
		{
			return "";
		}
		int type = (int)row.Type;
		if (row.Data == null)
		{
			if (!TypeNames.Table.TryGetValue(type, out var tableName))
			{
				return "#" + type;
			}
			return tableName;
		}
		try
		{
			// 游戏对未解锁等级会返回占位名（“状态”开头），低等级时优先使用缓存里的真名。
			if (NameCache.TryGetValue(type, out var cachedShortName) && !string.IsNullOrEmpty(cachedShortName) && !cachedShortName.StartsWith("状态") && level < 2)
			{
				return cachedShortName;
			}
			if (level > 0)
			{
				string levelName = ResolveNameText(ResolveNameId(row, level));
				if (!string.IsNullOrEmpty(levelName))
				{
					return levelName;
				}
			}
			if (NameCache.TryGetValue(type, out var cachedFallbackName) && !string.IsNullOrEmpty(cachedFallbackName))
			{
				return cachedFallbackName;
			}
			string lv1Name = ResolveNameText(ResolveNameId(row, 1));
			if (!string.IsNullOrEmpty(lv1Name))
			{
				return lv1Name;
			}
		}
		catch
		{
		}
		if (TypeNames.Table.TryGetValue(type, out var fallbackTableName))
		{
			return fallbackTableName;
		}
		int typeId = (int)row.Type;
		return "异常 " + typeId;
	}

	/// <summary>安全地取得玩家异常状态列表；管理器不可用时返回 null。</summary>
	internal static AbnormalList GetAbnormalList()
	{
		try
		{
			PlayerStatusManager playerStatus = ManagerList.PlayerStatus;
			if (playerStatus == null)
			{
				return null;
			}
			return playerStatus.AbnormalList;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>重建“已激活状态”集合：遍历全部已知类型查询当前等级。</summary>
	private static void RebuildActiveSet()
	{
		_activeTypes.Clear();
		AbnormalList abnormalList = GetAbnormalList();
		if (abnormalList == null)
		{
			return;
		}
		int[] allTypeValues = AllTypeValues;
		for (int i = 0; i < allTypeValues.Length; i++)
		{
			try
			{
				if (abnormalList.GetAbnormalLevel((AbnormalType)allTypeValues[i]) > 0)
				{
					_activeTypes.Add(allTypeValues[i]);
				}
			}
			catch
			{
			}
		}
	}

	/// <summary>刷新面板上的“已挂:N”计数；未强制且数量未变时直接返回。</summary>
	private static void RefreshActiveCount(bool force)
	{
		if (UI == null || UI.ActiveCountText == null)
		{
			return;
		}
		int count = _activeTypes.Count;
		if (!force && count == _lastActiveCount)
		{
			return;
		}
		_lastActiveCount = count;
		try
		{
			// 注意：这里**不要**再顺手加日志。MelonLogger.Msg 走的是
			// NativeMsg(..., null, txt)，会触发 GetMelonFromStackTrace()
			// 里的 new StackTrace(3, true)（要解析文件名与行号），再加 native 写盘。
			// 而本方法会被 IsLastActiveStatus(force: true) 调用 —— 每关一个状态就打一行。
			// 面板上已经有「已挂:N」了，这里不需要额外留痕。
			UI.ActiveCountText.text = "已挂:" + count;
		}
		catch
		{
		}
	}

	/// <summary>判断当前是否只剩最后一个已激活状态（游戏不允许把列表清空）。</summary>
	private static bool IsLastActiveStatus()
	{
		try
		{
			RebuildActiveSet();
			RefreshActiveCount(force: true);
		}
		catch
		{
		}
		return _activeTypes.Count <= 1;
	}

	/// <summary>拒绝取消最后一个异常状态时的日志与提示。</summary>
	private static void WarnKeepLastStatus()
	{
		MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] refused to remove the last abnormal status (game UI locks up when the list is empty).");
		ShowHintWarning("注意：最后一个异常状态不能取消 —— 清空后游戏的异常状态界面会卡死，只能重启");
	}

	/// <summary>返回加护类状态的互斥对偶（不孕加护与丧失加护），其它类型返回 0。</summary>
	private static int OppositeProtection(int typeId)
	{
		return typeId switch
		{
			ProtectionBarren => ProtectionLoss,
			ProtectionLoss => ProtectionBarren,
			_ => 0, 
		};
	}

	/// <summary>两个加护必须二选一、不能都关时的日志与提示。</summary>
	private static void WarnProtectionPair()
	{
		MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] refused to remove the last protection buff (barren/loss must keep exactly one).");
		ShowHintWarning("不孕的加护 / 丧失加护 是二选一的：想换直接点另一个，不能两个都关");
	}

	/// <summary>加护互换失败后恢复被移除的那个加护；恢复也失败时提示用户重试。</summary>
	private static void RestoreProtection(AbnormalList list, int typeId, int level)
	{
		if (list == null || typeId == 0 || level <= 0)
		{
			return;
		}
		try
		{
			list.AddAbnormal((AbnormalType)typeId, level);
			_activeTypes.Add(typeId);
			MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] protection pair: restored type " + typeId + " (Lv" + level + ") after a failed swap.");
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] protection pair: restore FAILED for type " + typeId + " (Lv" + level + "), msg=" + ex.Message);
			ShowHintWarning("加护互换没成功，请再点一次那个加护");
		}
	}

	/// <summary>
	/// 取一个 IL2CPP 对象的原生类名（例如 "AbnormalSlipDamage"）。
	/// 为什么不直接用 C# 的 GetType().Name：这游戏是 IL2CPP 编译的，真正的类型信息
	/// 在原生那一侧；直接问原生运行时拿到的名字才和游戏源码里的类名完全一致。
	/// </summary>
	private static string NativeClassName(Il2CppObjectBase obj)
	{
		if (obj == null)
		{
			return null;
		}
		try
		{
			IntPtr klass = IL2CPP.il2cpp_object_get_class(obj.Pointer);
			if (klass == IntPtr.Zero)
			{
				return null;
			}
			IntPtr namePtr = IL2CPP.il2cpp_class_get_name(klass);
			return (namePtr == IntPtr.Zero) ? null : Marshal.PtrToStringAnsi(namePtr);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// 现场判定：把一个异常状态写进存档，下次读档会不会崩。
	/// 返回 null 表示没发现问题；否则返回一句能直接给玩家看的原因。
	///
	/// 这个判定是「问游戏自己」而不是查表：游戏给每个状态挂的扩展组件就是它的行为，
	/// 含 SlipDamage / GameEnd / AutoDead 的都进不了存档，一看一个准，也不会随版本失效。
	/// </summary>
	private static string DetectSaveRisk(AbnormalData data)
	{
		if (data == null)
		{
			return null;
		}
		try
		{
			Il2CppReferenceArray<AbnormalExtension> extensions = data.m_AbnormalExtensionArray;
			if (extensions == null)
			{
				return null;
			}
			for (int i = 0; i < extensions.Length; i++)
			{
				AbnormalExtension extension = extensions[i];
				if (extension == null)
				{
					continue;
				}
				string className = NativeClassName(extension);
				if (string.IsNullOrEmpty(className))
				{
					continue;
				}
				if (className.Contains("SlipDamage"))
				{
					return "持续伤害类，写进存档后读档会崩溃";
				}
				if (className.Contains("GameEnd"))
				{
					return "游戏结束演出类，写进存档后读档会卡住";
				}
				if (className.Contains("AutoDead"))
				{
					return "自动死亡判定类，写进存档后读档会卡住";
				}
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>
	/// 判定并记下一个状态的风险，结果缓存起来（每种状态只判定一次）。
	///
	/// 只有**判定出风险**的才写日志（实测 70 个里只有 4 个）。
	/// 早先是每个状态都写一行，但 70 行里 66 行是 "safe"，信息密度太低，
	/// 而每行都要付一次栈回溯 + native 写盘 —— 不值当。
	/// 安全的那些看 RiskyStatuses 名单与 DetectSaveRisk 的判据即可。
	/// </summary>
	private static string JudgeSaveRisk(int typeId, AbnormalData data)
	{
		if (SaveRisks.TryGetValue(typeId, out string cached))
		{
			return cached;
		}
		if (data == null)
		{
			return null;
		}
		string risk = DetectSaveRisk(data);
		SaveRisks[typeId] = risk;
		if (risk != null)
		{
			MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] save-risk: " + GetDisplayName(typeId) + " (" + typeId + ") -> " + risk);
		}
		return risk;
	}

	/// <summary>查一个状态已知的风险（不触发新判定）。判定过就用判定结果，否则退回兜底名单。</summary>
	private static string KnownSaveRisk(int typeId)
	{
		// 运行时判定优先 —— 它看的是游戏真正挂上去的扩展组件，最准。
		if (SaveRisks.TryGetValue(typeId, out string judged) && judged != null)
		{
			return judged;
		}
		// 判定不到（例如没加载到数据），或判定为安全但属于「API 层面危险」的，退回静态名单。
		return RiskyStatuses.TryGetValue(typeId, out string known) ? known : null;
	}

	/// <summary>
	/// 取一个状态在面板上显示的名字 —— 必须与行内显示完全一致。
	///
	/// 为什么不直接用 TypeNames.Table：那张表只是**最后一层兜底**。
	/// 行内的名字走 GetStatusName，它会先问游戏本地化，取不到才轮到表；
	/// 而警告文案若直接取表，同一个状态就会出现
	/// 「面板上叫黑暗、提示里写束缚」这种两个名字的情况，玩家会以为是两回事。
	/// </summary>
	private static string GetDisplayName(int typeId)
	{
		// NameCache 由 CacheRowName 填好，装的正是行内显示用的那个名字。
		if (NameCache.TryGetValue(typeId, out string cached) && !string.IsNullOrEmpty(cached))
		{
			return cached;
		}
		// 缓存还没建（这一行还没刷新过）：按 typeId 找到那一行，走同一套解析。
		for (int i = 0; i < Rows.Count; i++)
		{
			StatusRow row = Rows[i];
			if (row == null || (int)row.Type != typeId)
			{
				continue;
			}
			try
			{
				string resolved = GetStatusName(row, 0);
				if (!string.IsNullOrEmpty(resolved))
				{
					return resolved;
				}
			}
			catch
			{
			}
			break;
		}
		// 实在拿不到（行还没建）才退回类型名表。
		return TypeNames.Table.TryGetValue(typeId, out string fallback) ? fallback : ("ID " + typeId);
	}

	/// <summary>危险状态第一次被点：只给黄色警告，什么都不做。</summary>
	private static void WarnRiskyFirstClick(int typeId)
	{
		string name = GetDisplayName(typeId);
		string reason = RiskyStatuses.TryGetValue(typeId, out string known) ? known : "有风险";
		MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] risky status first click: " + name + " (" + typeId + ") - " + reason + "; not applied, needs a second click.");
		ShowHintWarning("⚠ " + name + "：" + reason + "。这个状态可能会导致坏档，建议测试之前先存档 —— 再点一次才会真正开启");
	}

	/// <summary>危险状态第二次被点：转成红色警告，然后放行。</summary>
	private static void WarnRiskySecondClick(int typeId)
	{
		string name = GetDisplayName(typeId);
		MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] risky status confirmed by second click: " + name + " (" + typeId + "); applying now.");
		ShowHintWarning("⚠ 已确认开启「" + name + "」：如果游戏卡住、黑屏或读档失败，请直接重启游戏", armed: true);
	}

	/// <summary>拼文本、把提示栏点亮成警告色，并写一条 Warning 日志。</summary>
	private static void ShowSaveRiskWarning(int typeId, string risk)
	{
		string name = GetDisplayName(typeId);
		MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] SAVE RISK: " + name + " (" + typeId + ") - " + risk + ". Applied anyway; warning only.");
		ShowHintWarning("⚠ " + name + "：" + risk + "，存档可能作废（已照常开启，请谨慎存档）");
	}

	/// <summary>
	/// 玩家刚把一个状态打开时调用：若是高危状态就警告一句。
	/// 只警告、不阻止 —— 面板照常把它挂上。
	/// </summary>
	private static void WarnSaveRiskOnApply(StatusRow row, int typeId)
	{
		try
		{
			string risk = KnownSaveRisk(typeId);
			if (risk == null && row != null)
			{
				risk = JudgeSaveRisk(typeId, row.Data);
			}
			if (risk == null || !RiskWarned.Add(typeId))
			{
				return;
			}
			ShowSaveRiskWarning(typeId, risk);
		}
		catch
		{
		}
	}

	/// <summary>
	/// 第二道保险：面板开着的时候每秒扫一次「当前已挂的状态」。
	/// 这样即使高危状态是读档时自带的（不是这次点开的），玩家也能看到警告。
	/// </summary>
	private static void StepSaveRiskNotice()
	{
		if (Time.time < _riskNoticeCooldown)
		{
			return;
		}
		_riskNoticeCooldown = Time.time + 1f;
		if (_activeTypes.Count == 0)
		{
			return;
		}
		foreach (int typeId in _activeTypes)
		{
			string risk = KnownSaveRisk(typeId);
			if (risk == null || !RiskWarned.Add(typeId))
			{
				continue;
			}
			ShowSaveRiskWarning(typeId, risk);
			// 一次只提醒一个，免得连续覆盖提示栏。
			return;
		}
	}

	/// <summary>在面板提示栏显示 6 秒警告文本，到时间由 RestoreHint 还原。</summary>
	private static void ShowHintWarning(string message)
	{
		ShowHintWarning(message, armed: false);
	}

	/// <summary>同上，但可以选更重的红色（armed=true）—— 用在「即将真正生效」那一步。</summary>
	private static void ShowHintWarning(string message, bool armed)
	{
		// 6 秒后由 Tick 调用 RestoreHint 还原提示栏。
		_hintWarnUntil = Time.time + HintWarnSeconds;
		try
		{
			if (UI != null && UI.HintText != null)
			{
				UI.HintText.text = message;
				UI.HintText.color = (armed ? PanelBuilder.HintErrorColor : PanelBuilder.HintWarnColor);
			}
		}
		catch
		{
		}
	}

	/// <summary>把提示栏还原成默认的操作说明。</summary>
	private static void RestoreHint()
	{
		_hintWarnUntil = 0f;
		try
		{
			if (UI != null && UI.HintText != null)
			{
				// 文案由 PanelBuilder 按当前布局给（紧凑布局只有一行可用），
				// 不在这里写死 —— 否则低分辨率下面板会塞进一段放不下的长文案。
				UI.HintText.text = PanelBuilder.CurrentHint;
				UI.HintText.color = PanelBuilder.HintColor;
			}
		}
		catch
		{
		}
	}

	/// <summary>核心操作：把某行设为目标等级，处理加护互斥、最后一个状态保护与失败回滚。</summary>
	internal static void ApplyLevel(StatusRow row, int newLevel)
	{
		if (row == null)
		{
			return;
		}
		int type = (int)row.Type;
		if (row.Blocked || UnavailableTypes.Contains(type))
		{
			row.Blocked = true;
			// 点击被屏蔽的行只提示一次，避免刷日志。
			if (!_loggedBlockedClick)
			{
				_loggedBlockedClick = true;
				MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Clicked a blocked status: type " + type + " (ignored, no game API call)");
			}
			return;
		}
		if (row.Data == null)
		{
			EnsureTypeLoaded(row.Type);
			return;
		}
		AbnormalList abnormalList = GetAbnormalList();
		if (abnormalList == null)
		{
			return;
		}
		int maxLevel = 1;
		try
		{
			maxLevel = Mathf.Max(1, row.Data.MaxLevel);
		}
		catch
		{
		}
		int targetLevel = Mathf.Clamp(newLevel, 0, maxLevel);
		int currentLevel = 0;
		try
		{
			currentLevel = abnormalList.GetAbnormalLevel(row.Type);
		}
		catch
		{
		}
		if (currentLevel == targetLevel)
		{
			return;
		}
		// 危险状态开之前必须点两次：第一次只弹警告、不生效；第二次转红后才真正挂上。
		//
		// 为什么必须带 currentLevel <= 0：这里拦的是「从关到开」那一步。
		// 已经开着的危险状态再按 A/D 调等级时如果也拦，就变成升一级要点两下
		// （A/D 还支持长按连发），既烦又没意义 —— 风险在开启那一刻就已经承担了。
		// 关闭（targetLevel <= 0）同样不受影响，照常一下关掉。
		if (currentLevel <= 0 && targetLevel > 0 && RiskyStatuses.ContainsKey(type))
		{
			if (_pendingConfirmType != type)
			{
				_pendingConfirmType = type;
				WarnRiskyFirstClick(type);
				RefreshRows();
				return;
			}
			_pendingConfirmType = 0;
			WarnRiskySecondClick(type);
			// 这里不 return —— 继续往下把它真正挂上。
		}
		else if (_pendingConfirmType != 0)
		{
			// 玩家改点了别的状态：撤销上一次的待确认，免得过一会儿手滑点到就开了。
			_pendingConfirmType = 0;
			RefreshRows();
		}
		int oppositeType = OppositeProtection(type);
		int removedOppositeLevel = 0;
		// 加护是二选一的：设置新加护前先移除旧的那个，失败时回滚。
		if (oppositeType != 0)
		{
			int oppositeLevel = 0;
			try
			{
				oppositeLevel = abnormalList.GetAbnormalLevel((AbnormalType)oppositeType);
			}
			catch
			{
			}
			if (targetLevel <= 0)
			{
				if (oppositeLevel <= 0)
				{
					WarnProtectionPair();
					return;
				}
			}
			else if (oppositeLevel > 0)
			{
				try
				{
					abnormalList.RemoveAbnormal((AbnormalType)oppositeType);
					_activeTypes.Remove(oppositeType);
					removedOppositeLevel = oppositeLevel;
					MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] protection pair: swapped " + oppositeType + " -> " + type + ".");
				}
				catch (Exception ex)
				{
					MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] protection pair swap failed: type " + oppositeType + ", msg=" + ex.Message);
				}
			}
		}
		// 游戏在异常状态列表为空时会卡死，因此禁止取消最后一个状态。
		if (targetLevel <= 0 && currentLevel > 0 && IsLastActiveStatus())
		{
			WarnKeepLastStatus();
			return;
		}
		if (targetLevel > 0)
		{
			bool hasLevelData = false;
			bool probeFailed = false;
			try
			{
				// 目标等级没有对应数据，说明该状态无法通过 API 设置。
				hasLevelData = row.Data.GetAbnormalOne(targetLevel) != null;
			}
			catch
			{
				probeFailed = true;
			}
			if (probeFailed)
			{
				RestoreProtection(abnormalList, oppositeType, removedOppositeLevel);
				return;
			}
			if (!hasLevelData)
			{
				if (currentLevel <= 0)
				{
					MarkUnavailable(row);
				}
				else
				{
					MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] level " + targetLevel + " has no data for type " + type + " (currently Lv" + currentLevel + "), not blocking.");
				}
				RestoreProtection(abnormalList, oppositeType, removedOppositeLevel);
				return;
			}
		}
		try
		{
			if (targetLevel <= 0)
			{
				if (currentLevel > 0)
				{
					abnormalList.RemoveAbnormal(row.Type);
				}
			}
			else if (targetLevel < currentLevel)
			{
				// 降级必须走游戏的 DownAbnormal，不能拿 AddAbnormal 传一个更小的等级。
				//
				// AddAbnormal 对**已经挂着**的状态，语义是「升到指定等级」（内部取较高者），
				// 传更小的值不会把它降下来 —— 这就是「按 A 减不动、只有 Lv1 能按 A 清掉」
				// 的原因：那时 targetLevel 是 0，走的是上面 RemoveAbnormal 那条路。
				//
				// DownAbnormal 一次只降一级，所以按差值循环。
				int steps = currentLevel - targetLevel;
				for (int i = 0; i < steps; i++)
				{
					abnormalList.DownAbnormal(row.Type);
				}
				// 校验一次：游戏那边未必真的降下来了（个别状态可能对降级有内部保护）。
				// 不一致就记日志 —— 否则会出现「面板显示 Lv2、实际还挂着 Lv4」这种静默错位。
				try
				{
					int levelAfterDown = abnormalList.GetAbnormalLevel(row.Type);
					if (levelAfterDown != targetLevel)
					{
						MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] level change did not take effect: type " + type + " wanted Lv" + targetLevel + " but is Lv" + levelAfterDown);
					}
				}
				catch
				{
				}
			}
			else
			{
				abnormalList.AddAbnormal(row.Type, targetLevel);
			}
		}
		catch (Exception setError)
		{
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] set abnormal failed: type " + type + ", current=" + currentLevel + ", want=" + targetLevel + ", msg=" + setError.Message);
			try
			{
				if (currentLevel > 0)
				{
					abnormalList.AddAbnormal(row.Type, currentLevel);
				}
				else
				{
					abnormalList.RemoveAbnormal(row.Type);
				}
			}
			catch
			{
			}
			RestoreProtection(abnormalList, oppositeType, removedOppositeLevel);
			if (currentLevel > 0)
			{
				MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] level change failed for type " + type + " (" + currentLevel + " -> " + targetLevel + "), left unchanged");
			}
			else
			{
				MarkUnavailable(row);
			}
			try
			{
				RefreshRows();
				return;
			}
			catch
			{
				return;
			}
		}
		if (targetLevel > 0)
		{
			_activeTypes.Add(type);
			// 刚把这个状态打开：如果是会弄坏存档的那类，立刻提醒（只提醒，不阻止）。
			WarnSaveRiskOnApply(row, type);
		}
		else
		{
			_activeTypes.Remove(type);
		}
		RefreshActiveCount(force: false);
		try
		{
			RefreshRows();
		}
		catch
		{
		}
	}

	/// <summary>点击切换：当前等级大于 0 就关掉，否则设为 1 级。</summary>
	internal static void ToggleRow(StatusRow row)
	{
		if (row == null)
		{
			return;
		}
		// 数据尚未加载：先发起按需加载，本次点击不处理。
		if (row.Data == null)
		{
			EnsureTypeLoaded(row.Type);
			// 光 return 的话屏幕上毫无反馈，玩家会以为点了没反应 / 功能坏了。
			// 文案按「还在加载 / 已放弃重试」分开给，见 LoadingHintFor。
			ShowHintWarning(LoadingHintFor((int)row.Type));
			return;
		}
		int currentLevel = 0;
		try
		{
			AbnormalList abnormalList = GetAbnormalList();
			if (abnormalList != null)
			{
				currentLevel = abnormalList.GetAbnormalLevel(row.Type);
			}
		}
		catch
		{
		}
		ApplyLevel(row, (currentLevel <= 0) ? 1 : 0);
	}

	/// <summary>
	/// 数据还没到手时给玩家的提示。
	///
	/// 要分两种情况，不能一句话打发：
	///   · 正在加载 —— 再等一两秒就好；
	///   · 已经放弃重试（状态栏显示 ✗）—— 再怎么等也不会自己好，
	///     得关掉面板再打开才会重置重试次数。
	/// 只说「还在加载」的话，第二种情况的玩家会一直等下去。
	/// </summary>
	private static string LoadingHintFor(int type)
	{
		bool gaveUp = _loadAttempts.TryGetValue(type, out int attempts) && attempts >= MaxAutoLoadAttempts;
		return gaveUp
			? "这个状态加载不出来了，关掉面板再打开会重新尝试"
			: "这个状态还在加载数据，稍等一下再按一次";
	}

	/// <summary>按方向增减等级（dir 为 +1 或 -1），结果夹在 0 与最大等级之间。</summary>
	internal static void AdjustRowLevel(StatusRow row, int dir)
	{
		if (row == null)
		{
			return;
		}
		if (row.Data == null)
		{
			EnsureTypeLoaded(row.Type);
			// 同 ToggleRow：这里以前是静默 return，按 A/D 会「毫无反应」。
			ShowHintWarning(LoadingHintFor((int)row.Type));
			return;
		}
		int maxLevel = 1;
		int currentLevel = 0;
		try
		{
			maxLevel = Mathf.Max(1, row.Data.MaxLevel);
		}
		catch
		{
		}
		try
		{
			AbnormalList abnormalList = GetAbnormalList();
			if (abnormalList != null)
			{
				currentLevel = abnormalList.GetAbnormalLevel(row.Type);
			}
		}
		catch
		{
		}
		ApplyLevel(row, Mathf.Clamp(currentLevel + dir, 0, maxLevel));
	}

	/// <summary>每帧最多发起一次按需加载：只处理可视区附近且尚未取得数据的行。</summary>
	private static void StepLazyLoad()
	{
		if (Time.time < _lazyLoadCooldown || UI == null || Rows.Count == 0)
		{
			return;
		}
		float rowStride = UI.RowH + UI.RowSpacing;
		if (rowStride <= 0.01f)
		{
			return;
		}
		// 只对可视区附近的行发起加载，避免一次性加载全部 70 个类型。
		int firstVisible = Mathf.Max(0, Mathf.FloorToInt(UI.ScrollY / rowStride) - 1);
		int lastVisible = Mathf.Min(Rows.Count - 1, Mathf.CeilToInt((UI.ScrollY + UI.ViewHeight) / rowStride) + 1);
		for (int i = firstVisible; i <= lastVisible; i++)
		{
			StatusRow statusRow = Rows[i];
			if (statusRow == null || statusRow.Data != null || statusRow.Blocked)
			{
				continue;
			}
			int type = (int)statusRow.Type;
			if (UnavailableTypes.Contains(type))
			{
				continue;
			}
			if (_loadRequested.Contains(type))
			{
				// 超过 8 秒仍未加载完成，视为请求丢失，允许重新请求。
				if (_loadRequestTime.TryGetValue(type, out var requestTime) && !(Time.time - requestTime > LoadWaitTimeout))
				{
					continue;
				}
				_loadRequested.Remove(type);
				_loadRequestTime.Remove(type);
			}
			_loadAttempts.TryGetValue(type, out var attempts);
			// 每种类型最多自动尝试这么多次，超过就不再重试（避免对着一个加载不了的类型反复发请求）。
			if (attempts < MaxAutoLoadAttempts)
			{
				_loadAttempts[type] = attempts + 1;
				if (!EnsureTypeLoaded(statusRow.Type))
				{
					_lazyLoadCooldown = Time.time + 1f;
				}
				else
				{
					_lazyLoadCooldown = Time.time + 0.15f;
				}
				// 每帧最多发起一次加载请求，避免卡顿。
				break;
			}
		}
	}
}
