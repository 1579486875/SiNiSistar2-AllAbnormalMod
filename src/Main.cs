using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Il2Cpp;
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

public class Main : MelonMod
{
	private static string _modVersion;

	internal static readonly System.Collections.Generic.List<StatusRow> Rows = new System.Collections.Generic.List<StatusRow>();

	internal static GameObjectsRef UI;

	private static readonly int[] DangerousTypes = new int[3] { 1650800693, 1630627383, 912668513 };

	private const int ProtectionBarren = 825765986;

	private const int ProtectionLoss = 912668723;

	internal static readonly HashSet<int> UnavailableTypes = new HashSet<int>();

	private static bool _loggedBlockedClick;

	private static string _lastTickError;

	private static float _lastTickErrorTime;

	private static float _tickCooldown;

	private static float _lastRefresh;

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

	private static readonly Color BgSelectedColor = new Color(0.55f, 0.45f, 0.25f, 0.97f);

	private static readonly Color BgOnColor = new Color(0.16f, 0.24f, 0.18f, 0.95f);

	private static readonly Color BgOffColor = new Color(0.13f, 0.16f, 0.22f, 0.92f);

	private static readonly string[] LvText = BuildLvTextTable(20);

	private const int CodeBlocked = -2;

	private const int CodeLoading = -1;

	private const int CodeFailed = -3;

	private static readonly HashSet<int> _rowErrLogged = new HashSet<int>();

	internal static readonly System.Collections.Generic.Dictionary<int, string> NameCache = new System.Collections.Generic.Dictionary<int, string>();

	private static float _lazyLoadCooldown;

	private static Keyboard _kbCache;

	private static float _kbRetryTime;

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

	public override void OnInitializeMelon()
	{
		try
		{
			LoadBlockedList();
			for (int i = 0; i < DangerousTypes.Length; i++)
			{
				UnavailableTypes.Add(DangerousTypes[i]);
			}
			PanelInput.InitIl2Cpp();
			base.LoggerInstance.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Initialized. [Signature] 大赢经直插白皮赢道&汐蓝 [DYJ-CBWP-YD-XL]");
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

	private static void LoadBlockedList()
	{
		try
		{
			string blockedFilePath = BlockedFilePath;
			if (blockedFilePath == null || !File.Exists(blockedFilePath))
			{
				return;
			}
			string[] array = File.ReadAllLines(blockedFilePath);
			for (int i = 0; i < array.Length; i++)
			{
				string text = array[i].Trim();
				int num = text.IndexOf('#');
				if (num >= 0)
				{
					text = text.Substring(0, num).Trim();
				}
				if (text.Length != 0 && int.TryParse(text, out var result))
				{
					UnavailableTypes.Add(result);
				}
			}
			if (UnavailableTypes.Count > 0)
			{
				MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Loaded blocked status list: " + UnavailableTypes.Count + " entries.");
			}
		}
		catch
		{
		}
	}

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
					File.AppendAllText(blockedFilePath, "# One blocked status type per line. Delete a line to allow it again." + Environment.NewLine);
				}
				File.AppendAllText(blockedFilePath, type + "   # blocked automatically (cannot be set via game API) [DYJ-CBWP-YD-XL]" + Environment.NewLine);
			}
		}
		catch
		{
		}
	}

	private static void MarkUnavailable(StatusRow row)
	{
		if (row != null)
		{
			row.Blocked = true;
			int type = (int)row.Type;
			if (UnavailableTypes.Add(type))
			{
				SaveBlocked(type);
				MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] Status type " + type + " cannot be set through the game API; blocked (shown as x). Edit Mods/AllAbnormalMod_blocked.txt to unblock.");
			}
		}
	}

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
			_tickCooldown = Time.time + 2f;
			string message = ex.Message;
			if (message != _lastTickError || Time.time - _lastTickErrorTime > 15f)
			{
				_lastTickError = message;
				_lastTickErrorTime = Time.time;
				MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] Tick skipped (manager access restricted): " + message);
			}
		}
	}

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

	private static void ResetLoadCache()
	{
		_loadRequested.Clear();
		_loadAttempts.Clear();
		_loadRequestTime.Clear();
		_lazyLoadCooldown = 0f;
		_activeTypes.Clear();
		_activeSetDirty = true;
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

	internal static bool EnsureTypeLoaded(AbnormalType type)
	{
		int num = (int)type;
		if (_loadRequested.Contains(num))
		{
			return true;
		}
		_loadRequested.Add(num);
		_loadRequestTime[num] = Time.time;
		if (!_signatureLoggedLoad)
		{
			_signatureLoggedLoad = true;
			MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] on-demand loader active, first type " + num + " [大赢经直插白皮赢道&汐蓝]");
		}
		try
		{
			AbnormalManager abnormal = ManagerList.Abnormal;
			if (abnormal == null)
			{
				_loadRequested.Remove(num);
				_loadRequestTime.Remove(num);
				return false;
			}
			CancellationToken clt = new CancellationToken();
			abnormal.LoadAbnormalData(type, clt);
			return true;
		}
		catch
		{
			_loadRequested.Remove(num);
			_loadRequestTime.Remove(num);
			return false;
		}
	}

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
			AbnormalOriginalWrapper value = null;
			if (!abnormalDict.TryGetValue(type, out value))
			{
				return null;
			}
			return value?.OriginalData;
		}
		catch
		{
			return null;
		}
	}

	internal static void ClosePanelByUser()
	{
		PanelVisible = false;
		_suppressAutoShow = true;
		ClosedFrame = Time.frameCount;
		MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Panel closed by Home/Delete/Backspace.");
	}

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

	private static Keyboard GetCachedKeyboard()
	{
		if (_kbCache != null)
		{
			try
			{
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

	private static void Tick()
	{
		if (_hintWarnUntil > 0f && Time.time >= _hintWarnUntil)
		{
			RestoreHint();
		}
		try
		{
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
		bool flag = false;
		try
		{
			GalleryConditionUI cond = GetCond();
			if (cond != null && IsCondVisible(cond))
			{
				flag = true;
			}
		}
		catch
		{
		}
		if (!flag)
		{
			_suppressAutoShow = false;
		}
		bool flag2 = PanelVisible || (flag && !_suppressAutoShow);
		if (!flag2)
		{
			if (UI.Root.activeSelf)
			{
				UI.HideAuto();
			}
			try
			{
				Keyboard cachedKeyboard = GetCachedKeyboard();
				if (cachedKeyboard != null && ClosedFrame != Time.frameCount && (cachedKeyboard.homeKey.wasPressedThisFrame || cachedKeyboard.deleteKey.wasPressedThisFrame))
				{
					PanelVisible = true;
					_suppressAutoShow = false;
					OpenedFrame = Time.frameCount;
					flag2 = true;
					MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Panel opened by Home/Delete.");
				}
			}
			catch
			{
			}
		}
		if (!flag2)
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
		}
		try
		{
			StepLazyLoad();
		}
		catch
		{
		}
		float time = Time.time;
		if (_activeSetDirty && GetAbnormalList() != null)
		{
			_activeSetDirty = false;
			RebuildActiveSet();
			RefreshActiveCount(force: true);
		}
		if ((UI.RowsDirty && time - _lastRefresh >= 0.05f) || time - _lastRefresh > 0.5f)
		{
			UI.RowsDirty = false;
			_lastRefresh = time;
			RefreshRows();
			RefreshActiveCount(force: false);
		}
	}

	private static bool IsCondVisible(GalleryConditionUI cond)
	{
		if (cond == null)
		{
			return false;
		}
		try
		{
			GameObject gameObject = cond.gameObject;
			if (gameObject == null || !gameObject.activeInHierarchy)
			{
				return false;
			}
		}
		catch
		{
			return false;
		}
		if (_condGroupsOwner != cond)
		{
			_condGroupsOwner = cond;
			_condGroups.Clear();
			try
			{
				Transform transform = cond.GetComponent<RectTransform>();
				int num = 0;
				while (transform != null && num < 32)
				{
					try
					{
						CanvasGroup component = transform.GetComponent<CanvasGroup>();
						if (component != null)
						{
							_condGroups.Add(component);
						}
					}
					catch
					{
					}
					transform = transform.parent;
					num++;
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

	private static bool BuildUI()
	{
		try
		{
			GameObjectsRef gameObjectsRef = PanelBuilder.Build();
			if (gameObjectsRef == null || gameObjectsRef.Root == null)
			{
				return false;
			}
			UI = gameObjectsRef;
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

	internal static void SetSelected(int index)
	{
		int selectedIndex = _selectedIndex;
		_selectedIndex = index;
		_hasSelectedUi = true;
		RefreshRows();
		if (selectedIndex != index)
		{
			RefreshRowAt(selectedIndex);
		}
	}

	private static string[] BuildLvTextTable(int max)
	{
		string[] array = new string[max + 1];
		for (int i = 0; i <= max; i++)
		{
			array[i] = "Lv" + i;
		}
		return array;
	}

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
			bool flag = (statusRow.Blocked = statusRow.Blocked || UnavailableTypes.Contains((int)statusRow.Type));
			if (statusRow.Data == null && !flag && probeData)
			{
				statusRow.Data = TryGetData(statusRow.Type);
				if (statusRow.Data != null)
				{
					NameCache.Remove((int)statusRow.Type);
					CacheRowName(statusRow);
					statusRow.LastName = null;
				}
			}
			int num = 0;
			if (statusRow.Data != null)
			{
				if (list != null)
				{
					try
					{
						num = (statusRow.ModelLv = list.GetAbnormalLevel(statusRow.Type));
						if (num > 0)
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
						num = ((statusRow.ModelLv >= 0) ? statusRow.ModelLv : 0);
					}
				}
				else
				{
					num = ((statusRow.ModelLv >= 0) ? statusRow.ModelLv : 0);
				}
			}
			int value;
			int num2 = (flag ? (-2) : ((!(statusRow.Data != null)) ? ((_loadAttempts.TryGetValue((int)statusRow.Type, out value) && value >= 3) ? (-3) : (-1)) : num));
			if (statusRow.StateText != null && statusRow.LastStateCode != num2)
			{
				statusRow.LastStateCode = num2;
				string text;
				Color color;
				if (flag)
				{
					text = "×";
					color = StateUnavailableColor;
				}
				else
				{
					switch (num2)
					{
					case -3:
						text = "✗";
						color = StateUnavailableColor;
						break;
					case -1:
						text = "…";
						color = StateLoadingColor;
						break;
					default:
						if (num <= 0)
						{
							text = "OFF";
							color = StateOffColor;
						}
						else
						{
							text = ((num < LvText.Length) ? LvText[num] : ("Lv" + num));
							color = StateOnColor;
						}
						break;
					}
				}
				statusRow.StateText.text = text;
				statusRow.StateText.color = color;
			}
			if (num != statusRow.LastLv || statusRow.LastName == null || statusRow.LastNameBlocked != flag)
			{
				statusRow.LastLv = num;
				if (statusRow.NameText != null)
				{
					string statusName = GetStatusName(statusRow, num);
					if (statusRow.LastName != statusName || statusRow.LastNameBlocked != flag)
					{
						statusRow.LastName = statusName;
						statusRow.LastNameBlocked = flag;
						statusRow.NameText.text = statusName;
						statusRow.NameText.color = (flag ? NameBlockedColor : NameNormalColor);
					}
				}
			}
			bool flag2 = _hasSelectedUi && index == _selectedIndex;
			if (statusRow.Bg != null && (statusRow.LastSelected != flag2 || statusRow.LastBgLv != num))
			{
				statusRow.LastSelected = flag2;
				statusRow.LastBgLv = num;
				statusRow.Bg.color = (flag2 ? BgSelectedColor : ((num > 0) ? BgOnColor : BgOffColor));
			}
		}
		catch (Exception e)
		{
			LogRowError(index, e);
		}
	}

	private static void LogRowError(int index, Exception e)
	{
		if (_rowErrLogged.Add(index) && _rowErrLogged.Count <= 5)
		{
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] row " + index + " refresh failed: " + e.Message);
		}
	}

	internal static void RefreshRowAt(int index)
	{
		if (index >= 0 && index < Rows.Count)
		{
			RefreshRow(index, GetAbnormalList(), probeData: true);
		}
	}

	internal static void InitRowVisual(int index)
	{
		RefreshRow(index, null, probeData: false);
	}

	private static void RefreshRows()
	{
		if (Rows.Count == 0)
		{
			return;
		}
		AbnormalList abnormalList = GetAbnormalList();
		bool probeData = abnormalList != null;
		int num = 0;
		int num2 = Rows.Count - 1;
		if (UI != null)
		{
			float num3 = UI.RowH + UI.RowSpacing;
			float viewHeight = UI.ViewHeight;
			if (num3 > 0.01f && viewHeight > 1f)
			{
				num = Mathf.Max(0, Mathf.FloorToInt(UI.ScrollY / num3) - 1);
				num2 = Mathf.Min(Rows.Count - 1, Mathf.CeilToInt((UI.ScrollY + viewHeight) / num3) + 1);
			}
		}
		if (num > num2)
		{
			num = num2;
		}
		for (int i = num; i <= num2; i++)
		{
			RefreshRow(i, abnormalList, probeData);
		}
	}

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

	private static bool IsUsableName(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return false;
		}
		if (s.Length > 14)
		{
			return false;
		}
		if (s.IndexOf("[[", StringComparison.Ordinal) >= 0)
		{
			return false;
		}
		if (s.IndexOf('\n') >= 0)
		{
			return false;
		}
		return true;
	}

	private static string ResolveNameText(int nameId)
	{
		if (nameId <= 0)
		{
			return null;
		}
		string text = null;
		try
		{
			LocalizeManager localize = ManagerList.Localize;
			if (localize != null)
			{
				text = localize.GetLcText((LocalizeID)nameId);
			}
		}
		catch
		{
		}
		if (!string.IsNullOrEmpty(text))
		{
			return text;
		}
		if (ZhNames.Table.TryGetValue(nameId, out var value) && IsUsableName(value))
		{
			return value;
		}
		return null;
	}

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
		string value = null;
		int num = 1;
		try
		{
			num = Mathf.Clamp(row.Data.MaxLevel, 1, 20);
		}
		catch
		{
		}
		for (int i = 1; i <= num; i++)
		{
			string text = ResolveNameText(ResolveNameId(row, i));
			if (!string.IsNullOrEmpty(text))
			{
				value = text;
				break;
			}
		}
		if (string.IsNullOrEmpty(value))
		{
			value = ResolveNameText(ResolveNameId(row, 0));
		}
		if (string.IsNullOrEmpty(value))
		{
			value = (TypeNames.Table.TryGetValue(type, out var value2) ? value2 : null);
		}
		if (!string.IsNullOrEmpty(value))
		{
			NameCache[type] = value;
		}
	}

	internal static string GetStatusName(StatusRow row, int level)
	{
		if (row == null)
		{
			return "";
		}
		int type = (int)row.Type;
		if (row.Data == null)
		{
			if (!TypeNames.Table.TryGetValue(type, out var value))
			{
				return "#" + type;
			}
			return value;
		}
		try
		{
			if (NameCache.TryGetValue(type, out var value2) && !string.IsNullOrEmpty(value2) && !value2.StartsWith("状态") && level < 2)
			{
				return value2;
			}
			if (level > 0)
			{
				string text = ResolveNameText(ResolveNameId(row, level));
				if (!string.IsNullOrEmpty(text))
				{
					return text;
				}
			}
			if (NameCache.TryGetValue(type, out var value3) && !string.IsNullOrEmpty(value3))
			{
				return value3;
			}
			string text2 = ResolveNameText(ResolveNameId(row, 1));
			if (!string.IsNullOrEmpty(text2))
			{
				return text2;
			}
		}
		catch
		{
		}
		if (TypeNames.Table.TryGetValue(type, out var value4))
		{
			return value4;
		}
		int type2 = (int)row.Type;
		return "异常 " + type2;
	}

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
			UI.ActiveCountText.text = "已挂:" + count;
			MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] active abnormal count = " + count);
		}
		catch
		{
		}
	}

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

	private static void WarnKeepLastStatus()
	{
		MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] refused to remove the last abnormal status (game UI locks up when the list is empty).");
		ShowHintWarning("注意：最后一个异常状态不能取消 —— 清空后游戏的异常状态界面会卡死，只能重启");
	}

	private static int OppositeProtection(int typeId)
	{
		return typeId switch
		{
			825765986 => 912668723, 
			912668723 => 825765986, 
			_ => 0, 
		};
	}

	private static void WarnProtectionPair()
	{
		MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] refused to remove the last protection buff (barren/loss must keep exactly one).");
		ShowHintWarning("不孕的加护 / 丧失加护 是二选一的：想换直接点另一个，不能两个都关");
	}

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

	private static void ShowHintWarning(string message)
	{
		_hintWarnUntil = Time.time + 6f;
		try
		{
			if (UI != null && UI.HintText != null)
			{
				UI.HintText.text = message;
				UI.HintText.color = PanelBuilder.HintWarnColor;
			}
		}
		catch
		{
		}
	}

	private static void RestoreHint()
	{
		_hintWarnUntil = 0f;
		try
		{
			if (UI != null && UI.HintText != null)
			{
				UI.HintText.text = "Home / Delete：开关面板\u3000Backspace：关闭\nW/S 选择\u3000A/D 调等级（+/-）\u3000左键/右键点击切换\u3000滚轮滚动";
				UI.HintText.color = PanelBuilder.HintColor;
			}
		}
		catch
		{
		}
	}

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
		int max = 1;
		try
		{
			max = Mathf.Max(1, row.Data.MaxLevel);
		}
		catch
		{
		}
		int num = Mathf.Clamp(newLevel, 0, max);
		int num2 = 0;
		try
		{
			num2 = abnormalList.GetAbnormalLevel(row.Type);
		}
		catch
		{
		}
		if (num2 == num)
		{
			return;
		}
		int num3 = OppositeProtection(type);
		int level = 0;
		if (num3 != 0)
		{
			int num4 = 0;
			try
			{
				num4 = abnormalList.GetAbnormalLevel((AbnormalType)num3);
			}
			catch
			{
			}
			if (num <= 0)
			{
				if (num4 <= 0)
				{
					WarnProtectionPair();
					return;
				}
			}
			else if (num4 > 0)
			{
				try
				{
					abnormalList.RemoveAbnormal((AbnormalType)num3);
					_activeTypes.Remove(num3);
					level = num4;
					MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] protection pair: swapped " + num3 + " -> " + type + ".");
				}
				catch (Exception ex)
				{
					MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] protection pair swap failed: type " + num3 + ", msg=" + ex.Message);
				}
			}
		}
		if (num <= 0 && num2 > 0 && IsLastActiveStatus())
		{
			WarnKeepLastStatus();
			return;
		}
		if (num > 0)
		{
			bool flag = false;
			bool flag2 = false;
			try
			{
				flag = row.Data.GetAbnormalOne(num) != null;
			}
			catch
			{
				flag2 = true;
			}
			if (flag2)
			{
				RestoreProtection(abnormalList, num3, level);
				return;
			}
			if (!flag)
			{
				if (num2 <= 0)
				{
					MarkUnavailable(row);
				}
				else
				{
					MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] level " + num + " has no data for type " + type + " (currently Lv" + num2 + "), not blocking.");
				}
				RestoreProtection(abnormalList, num3, level);
				return;
			}
		}
		try
		{
			if (num <= 0)
			{
				if (num2 > 0)
				{
					abnormalList.RemoveAbnormal(row.Type);
				}
			}
			else
			{
				abnormalList.AddAbnormal(row.Type, num);
			}
		}
		catch (Exception ex2)
		{
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] set abnormal failed: type " + type + ", current=" + num2 + ", want=" + num + ", msg=" + ex2.Message);
			try
			{
				if (num2 > 0)
				{
					abnormalList.AddAbnormal(row.Type, num2);
				}
				else
				{
					abnormalList.RemoveAbnormal(row.Type);
				}
			}
			catch
			{
			}
			RestoreProtection(abnormalList, num3, level);
			if (num2 > 0)
			{
				MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] level change failed for type " + type + " (" + num2 + " -> " + num + "), left unchanged");
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
		if (num > 0)
		{
			_activeTypes.Add(type);
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

	internal static void ToggleRow(StatusRow row)
	{
		if (row == null)
		{
			return;
		}
		if (row.Data == null)
		{
			EnsureTypeLoaded(row.Type);
			return;
		}
		int num = 0;
		try
		{
			AbnormalList abnormalList = GetAbnormalList();
			if (abnormalList != null)
			{
				num = abnormalList.GetAbnormalLevel(row.Type);
			}
		}
		catch
		{
		}
		ApplyLevel(row, (num <= 0) ? 1 : 0);
	}

	internal static void AdjustRowLevel(StatusRow row, int dir)
	{
		if (row == null)
		{
			return;
		}
		if (row.Data == null)
		{
			EnsureTypeLoaded(row.Type);
			return;
		}
		int max = 1;
		int num = 0;
		try
		{
			max = Mathf.Max(1, row.Data.MaxLevel);
		}
		catch
		{
		}
		try
		{
			AbnormalList abnormalList = GetAbnormalList();
			if (abnormalList != null)
			{
				num = abnormalList.GetAbnormalLevel(row.Type);
			}
		}
		catch
		{
		}
		ApplyLevel(row, Mathf.Clamp(num + dir, 0, max));
	}

	private static void StepLazyLoad()
	{
		if (Time.time < _lazyLoadCooldown || UI == null || Rows.Count == 0)
		{
			return;
		}
		float num = UI.RowH + UI.RowSpacing;
		if (num <= 0.01f)
		{
			return;
		}
		int num2 = Mathf.Max(0, Mathf.FloorToInt(UI.ScrollY / num) - 1);
		int num3 = Mathf.Min(Rows.Count - 1, Mathf.CeilToInt((UI.ScrollY + UI.ViewHeight) / num) + 1);
		for (int i = num2; i <= num3; i++)
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
				if (_loadRequestTime.TryGetValue(type, out var value) && !(Time.time - value > 8f))
				{
					continue;
				}
				_loadRequested.Remove(type);
				_loadRequestTime.Remove(type);
			}
			_loadAttempts.TryGetValue(type, out var value2);
			if (value2 < 3)
			{
				_loadAttempts[type] = value2 + 1;
				if (!EnsureTypeLoaded(statusRow.Type))
				{
					_lazyLoadCooldown = Time.time + 1f;
				}
				else
				{
					_lazyLoadCooldown = Time.time + 0.15f;
				}
				break;
			}
		}
	}
}
