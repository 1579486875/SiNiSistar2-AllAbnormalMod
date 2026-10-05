using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSiNiSistar2.Obj;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace AllAbnormalMod;

public static class PanelBuilder
{
	internal const float ViewTopInset = 124f;

	/// <summary>上次构建面板时的屏幕分辨率。玩家在游戏设置里改了分辨率后，
	/// Main 会据此销毁并重建面板 —— 否则面板尺寸停留在旧分辨率上、会顶出屏幕。</summary>
	public static int BuiltScreenW;
	public static int BuiltScreenH;

	/// <summary>面板是不是在另一个分辨率下建的（玩家改了游戏画面设置）。
	/// Main 每帧检查一次，变过就把面板销毁重建，避免旧尺寸顶出屏幕。</summary>
	public static bool WasBuiltForDifferentScreen()
	{
		return BuiltScreenW > 0 && BuiltScreenH > 0
			&& (BuiltScreenW != Screen.width || BuiltScreenH != Screen.height);
	}

	private const float ViewBottomInset = 26f;

	internal const string HintDefault = "Home / Delete：开关面板\u3000Backspace：关闭\nW/S 选择\u3000A/D 调等级（+/-）\u3000左键/右键点击切换\u3000滚轮滚动";

	internal static readonly Color HintColor = new Color(1f, 1f, 1f, 0.62f);

	internal static readonly Color HintWarnColor = new Color(1f, 0.58f, 0.45f, 1f);

	public static string LastError = "";

	private static Canvas _canvas;

	private static Font _font;

	private static bool _fontWarned;

	private static bool _newRectTested;

	private static bool _newRectWorks;

	private static RectTransform _pendingContent;

	private static Font _pendingFont;

	private static int _pendingFontSize;

	private static GameObjectsRef _pendingUi;

	private static int _pendingStart;

	private static int _pendingTotal;

	private static int _pendingFail;

	private const string PanelRootName = "__AllAbnormalMod_Panel__[DYJ-CBWP-YD-XL]";

	public static bool HasPendingRows
	{
		get
		{
			if (_pendingTotal > 0)
			{
				return _pendingStart < _pendingTotal;
			}
			return false;
		}
	}

	private static Canvas EnsureCanvas()
	{
		try
		{
			if (_canvas != null && _canvas.gameObject != null)
			{
				return _canvas;
			}
		}
		catch
		{
			_canvas = null;
		}
		try
		{
			GameObject gameObject = new GameObject("__AllAbnormalMod_Canvas__[DYJ-CBWP-YD-XL]");
			Canvas canvas = gameObject.AddComponent<Canvas>();
			if (canvas == null)
			{
				LastError = "canvas component failed";
				return null;
			}
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 32000;
			canvas.overrideSorting = false;
			canvas.pixelPerfect = true;
			CanvasScaler canvasScaler = gameObject.AddComponent<CanvasScaler>();
			gameObject.AddComponent<GraphicRaycaster>();
			if (canvasScaler != null)
			{
				canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
				canvasScaler.scaleFactor = 1f;
				canvasScaler.referencePixelsPerUnit = 100f;
			}
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			_canvas = canvas;
			MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Overlay canvas created (screen-space, isolated from game UI).");
			return canvas;
		}
		catch (Exception ex)
		{
			LastError = "overlay canvas failed: " + ex.Message;
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] " + LastError);
			return null;
		}
	}

	private static RectTransform NewRect(string name, Transform parent)
	{
		if (_newRectTested && !_newRectWorks)
		{
			return null;
		}
		try
		{
			GameObject gameObject = new GameObject(name);
			if (gameObject == null)
			{
				_newRectTested = true;
				_newRectWorks = false;
				return null;
			}
			RectTransform rectTransform = gameObject.AddComponent<RectTransform>();
			if (rectTransform != null && gameObject.GetComponent<RectTransform>() != null)
			{
				if (parent != null)
				{
					rectTransform.SetParent(parent, worldPositionStays: false);
				}
				rectTransform.localScale = Vector3.one;
				rectTransform.localRotation = Quaternion.identity;
				rectTransform.localPosition = Vector3.zero;
				if (!_newRectTested)
				{
					_newRectTested = true;
					_newRectWorks = true;
					MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] UI is built from scratch (no game object cloning).");
				}
				return rectTransform;
			}
			_newRectTested = true;
			_newRectWorks = false;
			try
			{
				UnityEngine.Object.Destroy(gameObject);
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			if (!_newRectTested)
			{
				_newRectTested = true;
				_newRectWorks = false;
				MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] Cannot create RectTransform: " + ex.Message);
			}
		}
		return null;
	}

	private static Text CreateText(string name, Transform parent, Font font, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta, Color color, TextAnchor alignment, int fontSize)
	{
		RectTransform rectTransform = NewRect(name, parent);
		if (rectTransform == null)
		{
			return null;
		}
		try
		{
			GameObject gameObject = rectTransform.gameObject;
			Text text = gameObject.AddComponent<Text>();
			if (text == null)
			{
				try
				{
					UnityEngine.Object.DestroyImmediate(gameObject);
				}
				catch
				{
				}
				return null;
			}
			rectTransform.anchorMin = anchorMin;
			rectTransform.anchorMax = anchorMax;
			rectTransform.pivot = pivot;
			rectTransform.anchoredPosition = anchoredPos;
			rectTransform.sizeDelta = sizeDelta;
			text.font = font;
			text.text = "";
			text.color = color;
			text.alignment = alignment;
			text.fontSize = fontSize;
			text.fontStyle = FontStyle.Normal;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.verticalOverflow = VerticalWrapMode.Overflow;
			text.resizeTextForBestFit = false;
			text.lineSpacing = 1f;
			text.raycastTarget = false;
			try
			{
				Outline outline = gameObject.AddComponent<Outline>();
				if (outline != null)
				{
					outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
					outline.effectDistance = new Vector2(1f, -1f);
					outline.useGraphicAlpha = true;
				}
			}
			catch
			{
			}
			try
			{
				text.SetAllDirty();
			}
			catch
			{
			}
			return text;
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] CreateText failed: " + ex.Message);
			return null;
		}
	}

	private static Image AddImage(GameObject go, Color color, bool raycast)
	{
		try
		{
			Image image = go.AddComponent<Image>();
			if (image != null)
			{
				image.color = color;
				image.raycastTarget = raycast;
			}
			return image;
		}
		catch
		{
			return null;
		}
	}

	private static Font TryOsFont()
	{
		try
		{
			return Font.CreateDynamicFontFromOSFont(new string[4] { "Microsoft YaHei", "微软雅黑", "SimHei", "黑体" }, 16);
		}
		catch
		{
			return null;
		}
	}

	private static Font ResolveFont()
	{
		try
		{
			Il2CppArrayBase<Text> il2CppArrayBase = Resources.FindObjectsOfTypeAll<Text>();
			if (il2CppArrayBase != null)
			{
				Font font = null;
				for (int i = 0; i < il2CppArrayBase.Count; i++)
				{
					Text text = il2CppArrayBase[i];
					if (text == null)
					{
						continue;
					}
					Font font2 = text.font;
					if (font2 == null)
					{
						continue;
					}
					if (font == null)
					{
						font = font2;
					}
					try
					{
						if (font2.HasCharacter('异') && font2.HasCharacter('状'))
						{
							return font2;
						}
					}
					catch
					{
					}
				}
				if (font != null)
				{
					Font font3 = TryOsFont();
					if (font3 != null)
					{
						return font3;
					}
					return font;
				}
			}
		}
		catch
		{
		}
		try
		{
			Font font4 = TryOsFont();
			if (font4 != null)
			{
				if (!_fontWarned)
				{
					_fontWarned = true;
					MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] Falling back to an OS font for panel text.");
				}
				return font4;
			}
		}
		catch
		{
		}
		try
		{
			return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
		}
		catch
		{
		}
		return null;
	}

	internal static void DestroyStrayPanels()
	{
		if (_canvas == null)
		{
			return;
		}
		try
		{
			Transform transform = _canvas.transform;
			for (int num = transform.childCount - 1; num >= 0; num--)
			{
				Transform child = transform.GetChild(num);
				if (!(child == null))
				{
					GameObject gameObject = child.gameObject;
					if (gameObject != null && gameObject.name.StartsWith("__AllAbnormalMod_Panel__[DYJ-CBWP-YD-XL]"))
					{
						UnityEngine.Object.Destroy(gameObject);
					}
				}
			}
		}
		catch
		{
		}
	}

	public static GameObjectsRef Build()
	{
		LastError = "";
		Canvas canvas = EnsureCanvas();
		if (canvas == null)
		{
			return null;
		}
		Font font = _font;
		if (font == null)
		{
			font = (_font = ResolveFont());
		}
		if (font == null)
		{
			LastError = "no usable font";
			return null;
		}
		float screenW = Mathf.Max(320f, Screen.width);
		float screenH = Mathf.Max(240f, Screen.height);

		// 面板尺寸先按屏幕比例取，再硬性收进屏幕内（四周留 ScreenMargin）。
		// 低分辨率下（例如 720x360）原来的尺寸下界 420 比屏幕本身还高，面板会顶出屏幕，
		// 标题和状态名被裁掉一半 —— 所以比例取值之后必须再夹一次，屏幕尺寸是硬上限。
		float panelW = Mathf.Clamp(screenW * 0.36f, 420f, 780f);
		float panelH = Mathf.Clamp(screenH * 0.88f, 420f, 960f);
		const float ScreenMargin = 8f;
		panelW = Mathf.Min(panelW, Mathf.Max(160f, screenW - ScreenMargin * 2f));
		panelH = Mathf.Min(panelH, Mathf.Max(160f, screenH - ScreenMargin * 2f));

		// 面板矮时进入紧凑布局：提示压成一行、不显示分辨率提醒与底部签名，
		// 把纵向空间让给状态列表（否则 720x360 下顶部区域吃掉一半高度，只剩 9 行可看）。
		bool compact = panelH < 470f;
		float topInset = (compact ? 72f : 124f);
		float bottomInset = (compact ? 8f : 26f);

		// 顶部一行的横向分配：左边距 12 | 标题 | 间距 8 | 已挂 96 | 间距 8 | 页码 84 | 右边距 12
		// 标题横向必须用 offsetMin/offsetMax 钉边界（见下面 Title 处注释），
		// 两个常量保证「标题 / 已挂 / 页码」三者范围互不重叠。
		const float TopInset = 12f;
		const float TopRightBlockW = 196f;

		float rowH = Mathf.Clamp(screenH * 0.026f, 22f, 40f);
		int fontSize = Mathf.Clamp(Mathf.RoundToInt(rowH * 0.6f), 14, 24);
		GameObjectsRef gameObjectsRef = new GameObjectsRef();
		gameObjectsRef.RowH = rowH;
		gameObjectsRef.RowSpacing = 2f;
		RectTransform rectTransform = NewRect("__AllAbnormalMod_Panel__[DYJ-CBWP-YD-XL]", canvas.transform);
		if (rectTransform == null)
		{
			LastError = "panel root creation failed";
			return null;
		}
		rectTransform.anchorMin = new Vector2(1f, 0.5f);
		rectTransform.anchorMax = new Vector2(1f, 0.5f);
		rectTransform.pivot = new Vector2(1f, 0.5f);
		// 面板贴屏幕右侧居中。宽高已经被夹进屏幕内（见上面的 ScreenMargin），
		// 所以低分辨率下左边不会再跑到屏幕外、把状态名裁掉一半。
		rectTransform.anchoredPosition = new Vector2(-ScreenMargin, 0f);
		rectTransform.sizeDelta = new Vector2(panelW, panelH);
		GameObject gameObject = (gameObjectsRef.Root = rectTransform.gameObject);
		gameObjectsRef.RootRect = rectTransform;
		AddImage(gameObject, new Color(0.05f, 0.06f, 0.085f, 0.96f), raycast: true);
		// anchoredPosition.x 先给 0：标题的横向范围随后由 offsetMin/offsetMax 决定
		gameObjectsRef.Title = CreateText("Title", gameObject.transform, font, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(-220f, 30f), new Color(1f, 0.94f, 0.8f, 1f), TextAnchor.MiddleLeft, fontSize + 4);
		if (gameObjectsRef.Title != null)
		{
			// 紧凑布局下面板只有 420 宽，标题让位给「已挂」「页码」，改用短标题
			gameObjectsRef.Title.text = (compact
				? ("异常状态设定 v" + Main.ModVersion)
				: ("异常状态设定（Mod）  v" + Main.ModVersion));
			gameObjectsRef.Title.fontStyle = FontStyle.Bold;
			// 标题横向是「拉伸锚定」(anchorMin.x=0, anchorMax.x=1)。这种模式下
			// anchoredPosition.x 是 pivot 相对【锚框中心】的偏移，不是左边距 ——
			// 之前按左边距填 12，实际中心落在 panelW/2+12，标题整块右移、
			// 压到了「已挂:N」上。这里改用 offsetMin/offsetMax 直接钉住左右边缘。
			try
			{
				RectTransform titleRt = gameObjectsRef.Title.rectTransform;
				titleRt.offsetMin = new Vector2(TopInset, titleRt.offsetMin.y);
				titleRt.offsetMax = new Vector2(-(TopRightBlockW + TopInset), titleRt.offsetMax.y);
			}
			catch
			{
			}
		}
		gameObjectsRef.ScrollInfo = CreateText("ScrollInfo", gameObject.transform, font, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-12f, -20f), new Vector2(84f, 24f), new Color(0.7f, 0.85f, 1f, 0.9f), TextAnchor.MiddleRight, fontSize);
		if (gameObjectsRef.ScrollInfo != null)
		{
			gameObjectsRef.ScrollInfo.text = "0/0";
		}
		gameObjectsRef.ActiveCountText = CreateText("ActiveCount", gameObject.transform, font, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-104f, -20f), new Vector2(96f, 24f), new Color(0.7f, 0.85f, 1f, 0.9f), TextAnchor.MiddleRight, fontSize);
		if (gameObjectsRef.ActiveCountText != null)
		{
			gameObjectsRef.ActiveCountText.text = "已挂:0";
		}
		Text text = CreateText("Hint", gameObject.transform, font, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, compact ? -42f : -58f), new Vector2(panelW - 16f, compact ? 26f : 44f), HintColor, TextAnchor.MiddleCenter, Mathf.Clamp(fontSize - 3, 10, 16));
		if (text != null)
		{
			text.horizontalOverflow = HorizontalWrapMode.Wrap;
			// 紧凑布局（低分辨率）下只留一行最关键的操作提示，把高度让给列表
			text.text = compact
				? "Home/Delete 开关\u3000Backspace 关闭\u3000W/S 选择\u3000A/D 调等级\u3000左键点击切换"
				: "Home / Delete：开关面板\u3000Backspace：关闭\nW/S 选择\u3000A/D 调等级（+/-）\u3000左键/右键点击切换\u3000滚轮滚动";
		}
		gameObjectsRef.HintText = text;
		try
		{
			int num6 = 0;
			int num7 = 0;
			Il2CppStructArray<Resolution> resolutions = Screen.resolutions;
			for (int i = 0; i < ((Il2CppArrayBase<Resolution>)resolutions).Length; i++)
			{
				Resolution resolution = ((Il2CppArrayBase<Resolution>)resolutions)[i];
				if (resolution.width > num6)
				{
					num6 = resolution.width;
					num7 = resolution.height;
				}
			}
			bool flag = false;
			try
			{
				flag = Screen.fullScreen;
			}
			catch
			{
			}
			if (!compact && flag && num6 > 0 && Screen.width < num6 - 8)
			{
				Text text2 = CreateText("ResHint", gameObject.transform, font, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -104f), new Vector2(panelW - 16f, 38f), new Color(1f, 0.86f, 0.45f, 0.95f), TextAnchor.MiddleCenter, Mathf.Clamp(fontSize - 4, 10, 15));
				if (text2 != null)
				{
					text2.horizontalOverflow = HorizontalWrapMode.Wrap;
					text2.text = "提示：游戏分辨率 " + Screen.width + "x" + Screen.height + " 低于显示器的 " + num6 + "x" + num7 + "，文字会发糊 → 到「设置 → 画面」调高分辨率";
					MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Low render resolution detected: " + Screen.width + "x" + Screen.height + " (display is " + num6 + "x" + num7 + ")");
				}
			}
		}
		catch
		{
		}
		RectTransform rectTransform2 = NewRect("Viewport", gameObject.transform);
		if (rectTransform2 == null)
		{
			LastError = "viewport creation failed";
			try
			{
				UnityEngine.Object.Destroy(gameObject);
			}
			catch
			{
			}
			return null;
		}
		rectTransform2.anchorMin = Vector2.zero;
		rectTransform2.anchorMax = Vector2.one;
		rectTransform2.pivot = new Vector2(0.5f, 0.5f);
		rectTransform2.offsetMin = new Vector2(10f, bottomInset);
		rectTransform2.offsetMax = new Vector2(-10f, -topInset);
		try
		{
			rectTransform2.gameObject.AddComponent<RectMask2D>();
		}
		catch
		{
		}
		gameObjectsRef.Viewport = rectTransform2;
		RectTransform rectTransform3 = NewRect("Content", rectTransform2);
		if (rectTransform3 == null)
		{
			LastError = "content creation failed";
			try
			{
				UnityEngine.Object.Destroy(gameObject);
			}
			catch
			{
			}
			return null;
		}
		rectTransform3.anchorMin = new Vector2(0f, 1f);
		rectTransform3.anchorMax = new Vector2(1f, 1f);
		rectTransform3.pivot = new Vector2(0.5f, 1f);
		rectTransform3.offsetMin = new Vector2(0f, -10f);
		rectTransform3.offsetMax = new Vector2(0f, 0f);
		gameObjectsRef.Content = rectTransform3;
		RectTransform rectTransform4 = NewRect("ScrollTrack", gameObject.transform);
		if (rectTransform4 != null)
		{
			rectTransform4.anchorMin = new Vector2(1f, 0f);
			rectTransform4.anchorMax = new Vector2(1f, 1f);
			rectTransform4.pivot = new Vector2(1f, 0.5f);
			rectTransform4.offsetMin = new Vector2(-6f, bottomInset);
			rectTransform4.offsetMax = new Vector2(-2f, -topInset);
			AddImage(rectTransform4.gameObject, new Color(1f, 1f, 1f, 0.1f), raycast: false);
			RectTransform rectTransform5 = NewRect("ScrollBar", rectTransform4);
			if (rectTransform5 != null)
			{
				rectTransform5.anchorMin = new Vector2(0f, 1f);
				rectTransform5.anchorMax = new Vector2(1f, 1f);
				rectTransform5.pivot = new Vector2(0.5f, 1f);
				rectTransform5.offsetMin = new Vector2(0f, -24f);
				rectTransform5.offsetMax = new Vector2(0f, 0f);
				AddImage(rectTransform5.gameObject, new Color(0.62f, 0.82f, 1f, 0.55f), raycast: false);
				gameObjectsRef.ScrollTrack = rectTransform4;
				gameObjectsRef.ScrollBar = rectTransform5;
			}
		}
		Text text3 = CreateText("Signature", gameObject.transform, font, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-10f, 4f), new Vector2(panelW - 20f, 14f), new Color(1f, 1f, 1f, 0.45f), TextAnchor.LowerRight, Mathf.Clamp(fontSize - 5, 9, 12));
		if (text3 != null)
		{
			text3.text = (compact ? "" : "by 大赢经直插白皮赢道&汐蓝");
		}
		BuildRows(rectTransform3, font, fontSize, gameObjectsRef);
		try
		{
			PanelInput panelInput = gameObject.AddComponent<PanelInput>();
			if (panelInput != null)
			{
				panelInput.Init(gameObjectsRef, canvas);
			}
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] PanelInput attach failed: " + ex.Message);
		}
		BuiltScreenW = Screen.width;
		BuiltScreenH = Screen.height;
		MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Panel built on overlay canvas: screen " + screenW.ToString("F0") + "x" + screenH.ToString("F0") + ", panel " + panelW.ToString("F0") + "x" + panelH.ToString("F0") + ", rowH " + rowH.ToString("F1") + ", fontSize " + fontSize + (compact ? " (compact)" : ""));
		try
		{
			Resolution currentResolution = Screen.currentResolution;
			Vector2 vector = Vector2.zero;
			try
			{
				vector = canvas.renderingDisplaySize;
			}
			catch
			{
			}
			MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Display info: screen=" + Screen.width + "x" + Screen.height + ", desktop=" + currentResolution.width + "x" + currentResolution.height + ", canvasScale=" + canvas.scaleFactor.ToString("F2") + ", renderSize=" + vector.x.ToString("F0") + "x" + vector.y.ToString("F0") + ", font='" + font.name + "'");
		}
		catch
		{
		}
		return gameObjectsRef;
	}

	private static void BuildRows(RectTransform content, Font font, int fontSize, GameObjectsRef ui)
	{
		ClearPending();
		Main.Rows.Clear();
		int[] allTypeValues = Main.AllTypeValues;
		if (allTypeValues != null && allTypeValues.Length != 0)
		{
			float num = ui.RowH + ui.RowSpacing;
			content.offsetMin = new Vector2(0f, 0f - (float)allTypeValues.Length * num);
			content.offsetMax = new Vector2(0f, 0f);
			_pendingFont = font;
			_pendingFontSize = fontSize;
			_pendingContent = content;
			_pendingUi = ui;
			_pendingStart = 0;
			_pendingTotal = allTypeValues.Length;
			ui.ContentTotal = allTypeValues.Length;
			StepPendingRows(6);
		}
	}

	public static void ClearPending()
	{
		_pendingContent = null;
		_pendingFont = null;
		_pendingUi = null;
		_pendingStart = 0;
		_pendingTotal = 0;
		_pendingFail = 0;
	}

	public static void StepPendingRows(int maxRows)
	{
		if (!HasPendingRows)
		{
			return;
		}
		GameObjectsRef pendingUi = _pendingUi;
		RectTransform pendingContent = _pendingContent;
		Font pendingFont = _pendingFont;
		if (pendingUi == null || pendingContent == null || pendingFont == null)
		{
			ClearPending();
			return;
		}
		int num = 0;
		while (_pendingStart < _pendingTotal && num < maxRows)
		{
			if (!BuildOneRow(_pendingStart, pendingUi, pendingContent, pendingFont, _pendingFontSize))
			{
				MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] row " + _pendingStart + " build failed (total " + (_pendingFail + 1) + "), skipping");
				_pendingStart++;
				_pendingFail++;
				if (_pendingFail > 3)
				{
					ClearPending();
					return;
				}
			}
			else
			{
				_pendingStart++;
				num++;
			}
		}
		if (!HasPendingRows)
		{
			_pendingContent = null;
			_pendingFont = null;
			FinalizeRows(pendingUi);
		}
		else
		{
			pendingUi.UpdateScrollInfo();
		}
	}

	private static void FinalizeRows(GameObjectsRef ui)
	{
		MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Status rows created: " + Main.Rows.Count);
		ui.ScrollY = 0f;
		ui.ApplyScroll();
		Main.SetSelected(0);
	}

	private static bool BuildOneRow(int index, GameObjectsRef ui, RectTransform content, Font font, int fontSize)
	{
		try
		{
			float rowH = ui.RowH;
			float num = rowH + ui.RowSpacing;
			float num2 = (float)index * num;
			RectTransform rectTransform = NewRect("Row_" + index, content);
			if (rectTransform == null)
			{
				return false;
			}
			rectTransform.anchorMin = new Vector2(0f, 1f);
			rectTransform.anchorMax = new Vector2(1f, 1f);
			rectTransform.pivot = new Vector2(0.5f, 1f);
			rectTransform.offsetMin = new Vector2(2f, 0f - (num2 + rowH));
			rectTransform.offsetMax = new Vector2(-2f, 0f - num2);
			StatusRow statusRow = new StatusRow();
			statusRow.Type = (AbnormalType)Main.AllTypeValues[index];
			statusRow.Bg = AddImage(rectTransform.gameObject, new Color(0.13f, 0.16f, 0.22f, 0.92f), raycast: false);
			statusRow.NameText = CreateText("Name", rectTransform, font, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-48f, 0f), new Vector2(-110f, 0f), new Color(1f, 1f, 1f, 0.95f), TextAnchor.MiddleLeft, fontSize);
			statusRow.StateText = CreateText("State", rectTransform, font, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(96f, rowH), new Color(0.7f, 0.9f, 0.75f, 1f), TextAnchor.MiddleRight, fontSize);
			Main.Rows.Add(statusRow);
			Main.InitRowVisual(Main.Rows.Count - 1);
			return true;
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] row " + index + " build failed: " + ex.Message);
			return false;
		}
	}
}
