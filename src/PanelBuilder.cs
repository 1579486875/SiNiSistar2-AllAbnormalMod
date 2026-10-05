using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSiNiSistar2.Obj;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace AllAbnormalMod;

/// <summary>
/// 「异常状态设定」面板的构建器：自建一个 ScreenSpaceOverlay Canvas（与游戏自带 UI 隔离），
/// 再纯代码搭出标题、提示、滚动视口与全部状态行，不克隆任何游戏对象。
/// 面板贴屏幕右侧垂直居中；状态行分帧创建（见 StepPendingRows），避免一次建 70 行卡住主线程。
/// 本类只负责「建」，运行时的滚动/选中/刷新状态放在 GameObjectsRef 与 Main 里。
/// </summary>
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

	/// <summary>
	/// 分帧建行是否还没做完（还有没建的状态行）。Main 据此决定要不要继续推进。
	/// </summary>
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

	/// <summary>
	/// 取得（必要时创建）面板专用的 overlay Canvas。
	/// 只建一次：之后复用 _canvas；若托管引用还在而 Unity 对象已被销毁，则捕获异常后重建。
	/// </summary>
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
			GameObject canvasGo = new GameObject("__AllAbnormalMod_Canvas__[DYJ-CBWP-YD-XL]");
			Canvas canvas = canvasGo.AddComponent<Canvas>();
			if (canvas == null)
			{
				LastError = "canvas component failed";
				return null;
			}
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			// 顶到所有游戏 UI 之上（游戏自带 Canvas 远低于 32000），保证面板不被遮住
			canvas.sortingOrder = 32000;
			canvas.overrideSorting = false;
			// 像素对齐：中文小字号不做半像素抗锯齿，避免发虚
			canvas.pixelPerfect = true;
			CanvasScaler canvasScaler = canvasGo.AddComponent<CanvasScaler>();
			canvasGo.AddComponent<GraphicRaycaster>();
			if (canvasScaler != null)
			{
				canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
				// 固定 1 倍缩放：面板布局按屏幕真实像素算，不随游戏 UI 缩放漂移
				canvasScaler.scaleFactor = 1f;
				canvasScaler.referencePixelsPerUnit = 100f;
			}
			// 跨场景保留：切场景时面板不跟着被销毁
			UnityEngine.Object.DontDestroyOnLoad(canvasGo);
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

	/// <summary>
	/// 创建一个纯代码的 RectTransform 节点（不克隆游戏对象），摆到父节点下并把 transform 归零。
	/// 首次调用会探测「能不能凭空建 RectTransform」，结果记在 _newRectWorks；探测失败后直接返回 null。
	/// </summary>
	private static RectTransform NewRect(string name, Transform parent)
	{
		if (_newRectTested && !_newRectWorks)
		{
			return null;
		}
		try
		{
			GameObject rectGo = new GameObject(name);
			if (rectGo == null)
			{
				_newRectTested = true;
				_newRectWorks = false;
				return null;
			}
			RectTransform rect = rectGo.AddComponent<RectTransform>();
			if (rect != null && rectGo.GetComponent<RectTransform>() != null)
			{
				if (parent != null)
				{
					rect.SetParent(parent, worldPositionStays: false);
				}
				rect.localScale = Vector3.one;
				rect.localRotation = Quaternion.identity;
				rect.localPosition = Vector3.zero;
				if (!_newRectTested)
				{
					_newRectTested = true;
					_newRectWorks = true;
					MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] UI is built from scratch (no game object cloning).");
				}
				return rect;
			}
			_newRectTested = true;
			_newRectWorks = false;
			try
			{
				UnityEngine.Object.Destroy(rectGo);
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

	/// <summary>
	/// 建一个 Text 节点：按参数摆好锚点/轴心/位置/尺寸，设好字体与对齐，再挂一层黑描边。
	/// 文本内容留空，由调用方随后填（面板上要按紧凑布局切长短两版文案）。
	/// </summary>
	private static Text CreateText(string name, Transform parent, Font font, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta, Color color, TextAnchor alignment, int fontSize)
	{
		RectTransform textRt = NewRect(name, parent);
		if (textRt == null)
		{
			return null;
		}
		try
		{
			GameObject textGo = textRt.gameObject;
			Text textComp = textGo.AddComponent<Text>();
			if (textComp == null)
			{
				try
				{
					UnityEngine.Object.DestroyImmediate(textGo);
				}
				catch
				{
				}
				return null;
			}
			textRt.anchorMin = anchorMin;
			textRt.anchorMax = anchorMax;
			textRt.pivot = pivot;
			textRt.anchoredPosition = anchoredPos;
			textRt.sizeDelta = sizeDelta;
			textComp.font = font;
			textComp.text = "";
			textComp.color = color;
			textComp.alignment = alignment;
			textComp.fontSize = fontSize;
			textComp.fontStyle = FontStyle.Normal;
			// 默认单行不溢出；需要换行的提示行由调用方改成 Wrap
			textComp.horizontalOverflow = HorizontalWrapMode.Overflow;
			textComp.verticalOverflow = VerticalWrapMode.Overflow;
			textComp.resizeTextForBestFit = false;
			textComp.lineSpacing = 1f;
			// 文字不吃鼠标事件，点击穿透到底下的整行背景，由 PanelInput 统一处理
			textComp.raycastTarget = false;
			try
			{
				// 描边是可选装饰：挂不上也不该让整段文字建不出来
				Outline outline = textGo.AddComponent<Outline>();
				if (outline != null)
				{
					// 黑描边 + 右下 1px 偏移：面板底色偏暗且半透明，描边让小字在亮背景上也读得清
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
				textComp.SetAllDirty();
			}
			catch
			{
			}
			return textComp;
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] CreateText failed: " + ex.Message);
			return null;
		}
	}

	/// <summary>
	/// 给节点挂一个纯色 Image 当背景或轨道。raycast 决定它吃不吃鼠标事件：面板底图要吃，装饰条不吃。
	/// </summary>
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

	/// <summary>
	/// 向系统要一套带中文字形的动态字体（Microsoft YaHei / 微软雅黑 / SimHei / 黑体，按顺序回退）。
	/// 这里给的 16 只是占位字号，实际渲染字号由 Text.fontSize 决定。
	/// </summary>
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

	/// <summary>
	/// 找一套能渲染中文的字体，按优先级：
	/// 1) 场景里已有 Text 正在用、且含「异」「状」字形的字体（观感与游戏 UI 一致）；
	/// 2) 系统字体（雅黑 / 黑体）；
	/// 3) Unity 内置 LegacyRuntime.ttf —— 不含中文，只保证不抛异常。
	/// </summary>
	private static Font ResolveFont()
	{
		try
		{
			Il2CppArrayBase<Text> allTexts = Resources.FindObjectsOfTypeAll<Text>();
			if (allTexts != null)
			{
				Font font = null;
				for (int i = 0; i < allTexts.Count; i++)
				{
					Text candidateText = allTexts[i];
					if (candidateText == null)
					{
						continue;
					}
					Font candidateFont = candidateText.font;
					if (candidateFont == null)
					{
						continue;
					}
					if (font == null)
					{
						font = candidateFont;
					}
					try
					{
						// 拿面板标题里的「异」「状」当探针：能画出这两个字，才算这套字体带中文
						if (candidateFont.HasCharacter('异') && candidateFont.HasCharacter('状'))
						{
							return candidateFont;
						}
					}
					catch
					{
					}
				}
				if (font != null)
				{
					// 场景里虽然有字体但不含中文：优先换成系统字体，实在没有才退回它（至少英文能显示）
					Font osFont = TryOsFont();
					if (osFont != null)
					{
						return osFont;
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
			Font osFontFallback = TryOsFont();
			if (osFontFallback != null)
			{
				if (!_fontWarned)
				{
					_fontWarned = true;
					MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] Falling back to an OS font for panel text.");
				}
				return osFontFallback;
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

	/// <summary>
	/// 清掉 Canvas 上残留的面板根节点（重建面板前先扫一遍，防止旧的没收干净）。
	/// 从后往前遍历子节点：Destroy 只是打标记、帧末才真正移除，倒序才不会因索引错位漏删。
	/// </summary>
	internal static void DestroyStrayPanels()
	{
		if (_canvas == null)
		{
			return;
		}
		try
		{
			Transform canvasTr = _canvas.transform;
			// 倒序遍历：Destroy 只是标记删除、帧末才真正移除，正序会因索引错位漏删
			for (int childIndex = canvasTr.childCount - 1; childIndex >= 0; childIndex--)
			{
				Transform childTr = canvasTr.GetChild(childIndex);
				if (!(childTr == null))
				{
					GameObject childGo = childTr.gameObject;
					if (childGo != null && childGo.name.StartsWith("__AllAbnormalMod_Panel__[DYJ-CBWP-YD-XL]"))
					{
						UnityEngine.Object.Destroy(childGo);
					}
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// 完整构建一次面板：测尺寸 → 建根节点 → 标题/计数/提示 → 滚动视口与滚动条 → 分帧建状态行 → 挂输入组件。
	/// 尺寸先按屏幕比例取，再硬夹进屏幕内（低分辨率下不会顶出屏幕）；
	/// 面板高度不足 470 时切紧凑布局：提示压成一行、不显示分辨率提醒与签名，把纵向空间让给列表。
	/// </summary>
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
		// Screen 在启动早期可能返回 0：用 320x240 兜底，避免后面算出负的面板尺寸
		float screenW = Mathf.Max(320f, Screen.width);
		float screenH = Mathf.Max(240f, Screen.height);

		// 面板尺寸先按屏幕比例取，再硬性收进屏幕内（四周留 ScreenMargin）。
		// 低分辨率下（例如 720x360）原来的尺寸下界 420 比屏幕本身还高，面板会顶出屏幕，
		// 标题和状态名被裁掉一半 —— 所以比例取值之后必须再夹一次，屏幕尺寸是硬上限。
		float panelW = Mathf.Clamp(screenW * 0.36f, 420f, 780f);
		float panelH = Mathf.Clamp(screenH * 0.88f, 420f, 960f);
		// 面板与屏幕边缘至少留 8px：贴边会被显示器边缘裁掉
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
		// 196 = 「已挂」96 + 间距 8 + 页码 84 + 右边距 8 —— 标题的右边界据此让位
		const float TopRightBlockW = 196f;

		// 行高取屏高的 2.6%，夹在 22~40px：小屏不至于挤成一条线，大屏也不会一行占掉太多
		float rowH = Mathf.Clamp(screenH * 0.026f, 22f, 40f);
		// 字号跟行高联动（0.6 倍），夹在 14~24：行高缩小时文字不溢出，放大时也不撑破行
		int fontSize = Mathf.Clamp(Mathf.RoundToInt(rowH * 0.6f), 14, 24);
		GameObjectsRef ui = new GameObjectsRef();
		ui.RowH = rowH;
		// 行间距 2px：既能分清相邻行，又不至于把 70 行整体拉得过长
		ui.RowSpacing = 2f;
		RectTransform panelRt = NewRect("__AllAbnormalMod_Panel__[DYJ-CBWP-YD-XL]", canvas.transform);
		if (panelRt == null)
		{
			LastError = "panel root creation failed";
			return null;
		}
		panelRt.anchorMin = new Vector2(1f, 0.5f);
		panelRt.anchorMax = new Vector2(1f, 0.5f);
		panelRt.pivot = new Vector2(1f, 0.5f);
		// 面板贴屏幕右侧居中。宽高已经被夹进屏幕内（见上面的 ScreenMargin），
		// 所以低分辨率下左边不会再跑到屏幕外、把状态名裁掉一半。
		panelRt.anchoredPosition = new Vector2(-ScreenMargin, 0f);
		panelRt.sizeDelta = new Vector2(panelW, panelH);
		GameObject panelGo = (ui.Root = panelRt.gameObject);
		ui.RootRect = panelRt;
		AddImage(panelGo, new Color(0.05f, 0.06f, 0.085f, 0.96f), raycast: true);
		// anchoredPosition.x 先给 0：标题的横向范围随后由 offsetMin/offsetMax 决定
		// sizeDelta.x = -220 是初值：左边距 12 + 右侧「已挂/页码」块 196 + 右边距 12；
		// 真实横向范围稍后由 offsetMin/offsetMax 钉死
		ui.Title = CreateText("Title", panelGo.transform, font, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(-220f, 30f), new Color(1f, 0.94f, 0.8f, 1f), TextAnchor.MiddleLeft, fontSize + 4);
		if (ui.Title != null)
		{
			// 紧凑布局下面板只有 420 宽，标题让位给「已挂」「页码」，改用短标题
			ui.Title.text = (compact
				? ("异常状态设定 v" + Main.ModVersion)
				: ("异常状态设定（Mod）  v" + Main.ModVersion));
			ui.Title.fontStyle = FontStyle.Bold;
			// 标题横向是「拉伸锚定」(anchorMin.x=0, anchorMax.x=1)。这种模式下
			// anchoredPosition.x 是 pivot 相对【锚框中心】的偏移，不是左边距 ——
			// 之前按左边距填 12，实际中心落在 panelW/2+12，标题整块右移、
			// 压到了「已挂:N」上。这里改用 offsetMin/offsetMax 直接钉住左右边缘。
			try
			{
				RectTransform titleRt = ui.Title.rectTransform;
				titleRt.offsetMin = new Vector2(TopInset, titleRt.offsetMin.y);
				titleRt.offsetMax = new Vector2(-(TopRightBlockW + TopInset), titleRt.offsetMax.y);
			}
			catch
			{
			}
		}
		// 页码「n/N」贴右上角：右边距 12px、宽 84px、高 24px
		ui.ScrollInfo = CreateText("ScrollInfo", panelGo.transform, font, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-12f, -20f), new Vector2(84f, 24f), new Color(0.7f, 0.85f, 1f, 0.9f), TextAnchor.MiddleRight, fontSize);
		if (ui.ScrollInfo != null)
		{
			ui.ScrollInfo.text = "0/0";
		}
		// 「已挂:N」紧挨页码左侧：104 = 右边距 12 + 页码宽 84 + 间距 8
		ui.ActiveCountText = CreateText("ActiveCount", panelGo.transform, font, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-104f, -20f), new Vector2(96f, 24f), new Color(0.7f, 0.85f, 1f, 0.9f), TextAnchor.MiddleRight, fontSize);
		if (ui.ActiveCountText != null)
		{
			ui.ActiveCountText.text = "已挂:0";
		}
		// 提示行紧随标题下方；紧凑布局上移到 -42，省下 16px 给状态列表
		Text hintText = CreateText("Hint", panelGo.transform, font, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, compact ? -42f : -58f), new Vector2(panelW - 16f, compact ? 26f : 44f), HintColor, TextAnchor.MiddleCenter, Mathf.Clamp(fontSize - 3, 10, 16));
		if (hintText != null)
		{
			hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
			// 紧凑布局（低分辨率）下只留一行最关键的操作提示，把高度让给列表
			hintText.text = compact
				? "Home/Delete 开关\u3000Backspace 关闭\u3000W/S 选择\u3000A/D 调等级\u3000左键点击切换"
				: "Home / Delete：开关面板\u3000Backspace：关闭\nW/S 选择\u3000A/D 调等级（+/-）\u3000左键/右键点击切换\u3000滚轮滚动";
		}
		ui.HintText = hintText;
		try
		{
			// 先扫出显示器支持的最大分辨率，用来判断当前渲染分辨率是不是偏低
			int displayW = 0;
			int displayH = 0;
			Il2CppStructArray<Resolution> availableResolutions = Screen.resolutions;
			for (int i = 0; i < ((Il2CppArrayBase<Resolution>)availableResolutions).Length; i++)
			{
				Resolution res = ((Il2CppArrayBase<Resolution>)availableResolutions)[i];
				if (res.width > displayW)
				{
					displayW = res.width;
					displayH = res.height;
				}
			}
			// Screen.fullScreen 在个别平台会抛异常，所以单独 try：取不到就按窗口模式处理
			bool isFullScreen = false;
			try
			{
				isFullScreen = Screen.fullScreen;
			}
			catch
			{
			}
			// 只在「非紧凑 + 全屏 + 当前宽度明显小于显示器最大宽度」时提醒；-8 是给缩放/边框留的容差
			if (!compact && isFullScreen && displayW > 0 && Screen.width < displayW - 8)
			{
				// 分辨率提醒放在操作提示（-58）下方 46px 处，两行文字不重叠
				Text resHintText = CreateText("ResHint", panelGo.transform, font, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -104f), new Vector2(panelW - 16f, 38f), new Color(1f, 0.86f, 0.45f, 0.95f), TextAnchor.MiddleCenter, Mathf.Clamp(fontSize - 4, 10, 15));
				if (resHintText != null)
				{
					resHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
					resHintText.text = "提示：游戏分辨率 " + Screen.width + "x" + Screen.height + " 低于显示器的 " + displayW + "x" + displayH + "，文字会发糊 → 到「设置 → 画面」调高分辨率";
					MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Low render resolution detected: " + Screen.width + "x" + Screen.height + " (display is " + displayW + "x" + displayH + ")");
				}
			}
		}
		catch
		{
		}
		// 视口：左右各留 10px（右侧给滚动轨道），上下让开标题区与底部签名区
		RectTransform viewportRt = NewRect("Viewport", panelGo.transform);
		if (viewportRt == null)
		{
			LastError = "viewport creation failed";
			try
			{
				UnityEngine.Object.Destroy(panelGo);
			}
			catch
			{
			}
			return null;
		}
		viewportRt.anchorMin = Vector2.zero;
		viewportRt.anchorMax = Vector2.one;
		viewportRt.pivot = new Vector2(0.5f, 0.5f);
		viewportRt.offsetMin = new Vector2(10f, bottomInset);
		viewportRt.offsetMax = new Vector2(-10f, -topInset);
		try
		{
			// RectMask2D 裁掉超出视口的行；比 Mask 便宜，不需要额外材质
			viewportRt.gameObject.AddComponent<RectMask2D>();
		}
		catch
		{
		}
		ui.Viewport = viewportRt;
		// 内容层挂在视口下，纵向长度由 BuildRows 按行数撑开；滚动就是移动它的位置
		RectTransform contentRt = NewRect("Content", viewportRt);
		if (contentRt == null)
		{
			LastError = "content creation failed";
			try
			{
				UnityEngine.Object.Destroy(panelGo);
			}
			catch
			{
			}
			return null;
		}
		contentRt.anchorMin = new Vector2(0f, 1f);
		contentRt.anchorMax = new Vector2(1f, 1f);
		contentRt.pivot = new Vector2(0.5f, 1f);
		contentRt.offsetMin = new Vector2(0f, -10f);
		contentRt.offsetMax = new Vector2(0f, 0f);
		ui.Content = contentRt;
		RectTransform scrollTrackRt = NewRect("ScrollTrack", panelGo.transform);
		if (scrollTrackRt != null)
		{
			scrollTrackRt.anchorMin = new Vector2(1f, 0f);
			scrollTrackRt.anchorMax = new Vector2(1f, 1f);
			scrollTrackRt.pivot = new Vector2(1f, 0.5f);
			// 滚动轨道贴右边缘：距右 2px、宽 4px（-6 → -2），上下与视口对齐
			scrollTrackRt.offsetMin = new Vector2(-6f, bottomInset);
			scrollTrackRt.offsetMax = new Vector2(-2f, -topInset);
			AddImage(scrollTrackRt.gameObject, new Color(1f, 1f, 1f, 0.1f), raycast: false);
			RectTransform scrollBarRt = NewRect("ScrollBar", scrollTrackRt);
			if (scrollBarRt != null)
			{
				scrollBarRt.anchorMin = new Vector2(0f, 1f);
				scrollBarRt.anchorMax = new Vector2(1f, 1f);
				scrollBarRt.pivot = new Vector2(0.5f, 1f);
				// 滑块默认高 24px；实际位置与高度由 PanelInput 按滚动进度设置
				scrollBarRt.offsetMin = new Vector2(0f, -24f);
				scrollBarRt.offsetMax = new Vector2(0f, 0f);
				AddImage(scrollBarRt.gameObject, new Color(0.62f, 0.82f, 1f, 0.55f), raycast: false);
				ui.ScrollTrack = scrollTrackRt;
				ui.ScrollBar = scrollBarRt;
			}
		}
		// 右下角签名，距底 4px；紧凑布局下内容置空
		Text signatureText = CreateText("Signature", panelGo.transform, font, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-10f, 4f), new Vector2(panelW - 20f, 14f), new Color(1f, 1f, 1f, 0.45f), TextAnchor.LowerRight, Mathf.Clamp(fontSize - 5, 9, 12));
		if (signatureText != null)
		{
			signatureText.text = (compact ? "" : "by 大赢经直插白皮赢道&汐蓝");
		}
		BuildRows(contentRt, font, fontSize, ui);
		try
		{
			// 最后挂输入组件：滚轮/按键/点击都由它处理，需要拿到已经建好的 ui 引用
			PanelInput panelInput = panelGo.AddComponent<PanelInput>();
			if (panelInput != null)
			{
				panelInput.Init(ui, canvas);
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
			Resolution desktopRes = Screen.currentResolution;
			Vector2 renderSize = Vector2.zero;
			try
			{
				renderSize = canvas.renderingDisplaySize;
			}
			catch
			{
			}
			MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Display info: screen=" + Screen.width + "x" + Screen.height + ", desktop=" + desktopRes.width + "x" + desktopRes.height + ", canvasScale=" + canvas.scaleFactor.ToString("F2") + ", renderSize=" + renderSize.x.ToString("F0") + "x" + renderSize.y.ToString("F0") + ", font='" + font.name + "'");
		}
		catch
		{
		}
		return ui;
	}

	/// <summary>
	/// 开始建状态行：按行数把内容层撑到需要的高度，登记本批任务参数，然后先同步建 6 行。
	/// 剩下的交给 StepPendingRows 分帧继续 —— 一帧建完 70 行会明显卡顿。
	/// </summary>
	private static void BuildRows(RectTransform content, Font font, int fontSize, GameObjectsRef ui)
	{
		ClearPending();
		Main.Rows.Clear();
		int[] allTypeValues = Main.AllTypeValues;
		if (allTypeValues != null && allTypeValues.Length != 0)
		{
			// 每行占用的纵向步长 = 行高 + 行间距
			float rowStep = ui.RowH + ui.RowSpacing;
			content.offsetMin = new Vector2(0f, 0f - (float)allTypeValues.Length * rowStep);
			content.offsetMax = new Vector2(0f, 0f);
			_pendingFont = font;
			_pendingFontSize = fontSize;
			_pendingContent = content;
			_pendingUi = ui;
			_pendingStart = 0;
			_pendingTotal = allTypeValues.Length;
			ui.ContentTotal = allTypeValues.Length;
			// 先同步建 6 行，保证面板一出现列表就有内容；剩余的行由 Main 每帧继续推进
			StepPendingRows(6);
		}
	}

	/// <summary>
	/// 丢弃分帧建行的进度（重建面板、或连续失败时调用）。只清登记的上下文，已经建好的行不动。
	/// </summary>
	public static void ClearPending()
	{
		_pendingContent = null;
		_pendingFont = null;
		_pendingUi = null;
		_pendingStart = 0;
		_pendingTotal = 0;
		_pendingFail = 0;
	}

	/// <summary>
	/// 分帧建行的推进入口，由 Main 每帧调用一次。
	/// 每次最多建 maxRows 行；单行失败就跳过并累计，连续失败超过 3 行整体放弃（UI 环境已经不可用，再试没意义）。
	/// 全部建完则收尾（FinalizeRows），否则只刷新滚动信息。
	/// </summary>
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
		// 本帧真正建成的行数；失败跳过的行不占配额，避免一帧白跑
		int builtRows = 0;
		while (_pendingStart < _pendingTotal && builtRows < maxRows)
		{
			if (!BuildOneRow(_pendingStart, pendingUi, pendingContent, pendingFont, _pendingFontSize))
			{
				MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] row " + _pendingStart + " build failed (total " + (_pendingFail + 1) + "), skipping");
				_pendingStart++;
				_pendingFail++;
				// 连续失败上限：超过就说明 UI 环境已经不可用
				if (_pendingFail > 3)
				{
					ClearPending();
					return;
				}
			}
			else
			{
				_pendingStart++;
				builtRows++;
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

	/// <summary>
	/// 所有状态行建完后的收尾：滚回顶部，并把选中项复位到第 0 行。
	/// </summary>
	private static void FinalizeRows(GameObjectsRef ui)
	{
		MelonLogger.Msg("[AllAbnormalMod][DYJ-CBWP-YD-XL] Status rows created: " + Main.Rows.Count);
		ui.ScrollY = 0f;
		ui.ApplyScroll();
		Main.SetSelected(0);
	}

	/// <summary>
	/// 建一行状态项：整行背景 + 左侧状态名 + 右侧等级文字，并登记进 Main.Rows。
	/// 纵向位置用 offsetMin/offsetMax 直接钉 —— 该行是横向拉伸锚定，
	/// anchoredPosition.y 表示的是相对锚框中心的偏移，不是上边距。
	/// </summary>
	private static bool BuildOneRow(int index, GameObjectsRef ui, RectTransform content, Font font, int fontSize)
	{
		try
		{
			float rowH = ui.RowH;
			float rowStep = rowH + ui.RowSpacing;
			float rowTop = (float)index * rowStep;
			RectTransform rowRt = NewRect("Row_" + index, content);
			if (rowRt == null)
			{
				return false;
			}
			rowRt.anchorMin = new Vector2(0f, 1f);
			rowRt.anchorMax = new Vector2(1f, 1f);
			rowRt.pivot = new Vector2(0.5f, 1f);
			// 左右各留 2px 给行背景的描边；上边缘 -rowTop，下边缘 -(rowTop + rowH)
			rowRt.offsetMin = new Vector2(2f, 0f - (rowTop + rowH));
			rowRt.offsetMax = new Vector2(-2f, 0f - rowTop);
			StatusRow statusRow = new StatusRow();
			statusRow.Type = (AbnormalType)Main.AllTypeValues[index];
			statusRow.Bg = AddImage(rowRt.gameObject, new Color(0.13f, 0.16f, 0.22f, 0.92f), raycast: false);
			// 状态名贴左，右侧留 110px 给等级文字（等级文字宽 96 + 右边距 10 + 间隙 4）
			statusRow.NameText = CreateText("Name", rowRt, font, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-48f, 0f), new Vector2(-110f, 0f), new Color(1f, 1f, 1f, 0.95f), TextAnchor.MiddleLeft, fontSize);
			// 等级文字贴右：右边距 10px、宽 96px，与状态名区不重叠
			statusRow.StateText = CreateText("State", rowRt, font, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(96f, rowH), new Color(0.7f, 0.9f, 0.75f, 1f), TextAnchor.MiddleRight, fontSize);
			Main.Rows.Add(statusRow);
			// 按该行当前等级刷新背景与文字颜色
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
