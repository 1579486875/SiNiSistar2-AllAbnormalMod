using System;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AllAbnormalMod;

/// <summary>
/// 面板输入控制器（挂在面板根对象上）：统一轮询键盘与鼠标输入。
/// 键盘：W/S 上下选行、A/D 调整等级、PageUp/PageDown 翻页、End 跳到最后一行、Home/Delete/Backspace 关闭面板。
/// 鼠标：滚轮滚动列表、左键或右键点击某行切换其开关、按住标题栏或面板边缘拖动整个面板。
/// </summary>
public class PanelInput : MonoBehaviour
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

	internal const string InputTag = "DYJ-CBWP-YD-XL";

	private GameObjectsRef _ui;

	private Canvas _canvas;

	private Keyboard _kb;

	private Mouse _mouse;

	private float _kbRetry;

	private float _mouseRetry;

	private int _selected;

	private bool _hasSelection;

	private Vector2 _lastMousePos = new Vector2(-1f, -1f);

	private bool _dragging;

	private bool _suppressHoverOnce;

	private Vector2 _dragLastScreen;

	private string _lastError;

	/// <summary>长按 W/S 时用的连发计时器（累积到 RepeatInterval 就走一行）。</summary>
	private float _repeatMoveTimer;

	/// <summary>
	/// 长按 A/D/± 时用的连发计时器，**必须与移动那个分开**。
	///
	/// 早先两者共用同一个 _repeatTimer，而移动那边的清零条件里含
	/// `(!wHeld && !sHeld)` —— 玩家只按住 D、不碰 W/S 时这个条件恒为真，
	/// 于是计时器每帧被清零、永远攒不到 RepeatInterval，
	/// A/D/± 的长按连发实际上从来没生效过（只有按下那一帧会触发）。
	/// </summary>
	private float _repeatLevelTimer;

	private const float RepeatInterval = 0.16f;

	/// <summary>面板四周可拖动的边缘宽度（像素）：抓住边框就能挪面板。</summary>
	private const float DragBorder = 10f;

	/// <summary>
	/// 每次启用时清除拖动状态，并跳过第一帧的悬停判断（避免面板刚显示就被陈旧的鼠标位置误选中）。
	/// </summary>
	private void OnEnable()
	{
		_dragging = false;
		_dragLastScreen = default(Vector2);
		_suppressHoverOnce = true;
	}

	/// <summary>
	/// 绑定面板 UI 引用与所在 Canvas，并把选中状态重置为「未选中」。
	/// </summary>
	[HideFromIl2Cpp]
	public void Init(GameObjectsRef ui, Canvas canvas)
	{
		_ui = ui;
		_canvas = canvas;
		_selected = 0;
		_hasSelection = false;
	}

	/// <summary>
	/// 每帧处理键盘输入；面板未显示时直接跳过，异常只记录一次（避免刷屏）。
	/// </summary>
	public void Update()
	{
		try
		{
			if (_ui != null && !(_ui.Root == null) && _ui.Root.activeSelf)
			{
				HandleKeyboard();
			}
		}
		catch (Exception ex)
		{
			LogError("Keyboard", ex.Message);
		}
	}

	/// <summary>
	/// 销毁时释放引用，避免继续持有已卸载的 Unity 对象。
	/// </summary>
	private void OnDestroy()
	{
		_ui = null;
		_kb = null;
		_mouse = null;
	}

	/// <summary>
	/// 取当前键盘设备；取不到时每秒最多重试一次（Input System 设备未就绪时读取会抛异常）。
	/// </summary>
	private Keyboard GetKeyboard()
	{
		if (_kb != null)
		{
			try
			{
				if (_kb.added)
				{
					return _kb;
				}
			}
			catch
			{
			}
			_kb = null;
		}
		if (Time.time < _kbRetry)
		{
			return null;
		}
		_kbRetry = Time.time + 1f;
		try
		{
			_kb = Keyboard.current;
		}
		catch
		{
		}
		return _kb;
	}

	/// <summary>
	/// 取当前鼠标设备；重试节流策略与 GetKeyboard 相同。
	/// </summary>
	private Mouse GetMouse()
	{
		if (_mouse != null)
		{
			try
			{
				if (_mouse.added)
				{
					return _mouse;
				}
			}
			catch
			{
			}
			_mouse = null;
		}
		if (Time.time < _mouseRetry)
		{
			return null;
		}
		_mouseRetry = Time.time + 1f;
		try
		{
			_mouse = Mouse.current;
		}
		catch
		{
		}
		return _mouse;
	}

	/// <summary>
	/// 键盘主处理：上下选行（带长按连发）、调整等级、翻页、跳到最后一行、关闭面板。
	/// 所有下标运算都会 Clamp 到 [0, Rows.Count - 1]，因此外部改动行数时不会越界。
	/// </summary>
	private void HandleKeyboard()
	{
		Keyboard keyboard = GetKeyboard();
		if (keyboard == null)
		{
			return;
		}
		int rowCount = Main.Rows.Count;
		// 没有可操作的行时直接退出，后面所有下标计算都依赖它。
		if (rowCount == 0)
		{
			return;
		}
		bool wHeld = keyboard.wKey.isPressed;
		bool sHeld = keyboard.sKey.isPressed;
		bool wPressed = keyboard.wKey.wasPressedThisFrame;
		bool sPressed = keyboard.sKey.wasPressedThisFrame;
		// 移动的连发计时：刚按下、或两键都已松开时清零；按住不放则持续累积。
		if (wPressed || sPressed || (!wHeld && !sHeld))
		{
			_repeatMoveTimer = 0f;
		}
		else
		{
			_repeatMoveTimer += Time.deltaTime;
		}
		// 累积到一个间隔就触发一次重复。
		bool moveRepeatReady = _repeatMoveTimer >= RepeatInterval;
		if (moveRepeatReady)
		{
			// 减去一个间隔而不是清零，保留溢出的时间，低帧率下长按连发速度才不会被拖慢。
			_repeatMoveTimer -= RepeatInterval;
		}
		bool moveUp = wPressed || (wHeld && moveRepeatReady);
		bool moveDown = sPressed || (sHeld && moveRepeatReady);
		if (moveUp || moveDown)
		{
			if (!_hasSelection)
			{
				// 首次操作用户还没选过行：按当前滚动位置反推第一条可见行，作为选中起点。
				float rowStride = Mathf.Max(1f, _ui.RowH + _ui.RowSpacing);
				_selected = Mathf.Clamp(Mathf.FloorToInt(_ui.ScrollY / rowStride), 0, rowCount - 1);
			}
			int previousSelected = _selected;
			// W 向上（-1）、S 向下（+1）；越界由 Clamp 收口。
			_selected = Mathf.Clamp(_selected + ((!moveUp) ? 1 : (-1)), 0, rowCount - 1);
			if (_selected != previousSelected || !_hasSelection)
			{
				ApplySelection();
			}
		}
		// 降低等级：A / 主键盘 - / 小键盘 -。
		bool levelDownPressed = keyboard.aKey.wasPressedThisFrame || keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame;
		bool levelDownHeld = keyboard.aKey.isPressed || keyboard.minusKey.isPressed || keyboard.numpadMinusKey.isPressed;
		// 提升等级：D / 主键盘 = / 小键盘 +。
		bool levelUpPressed = keyboard.dKey.wasPressedThisFrame || keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame;
		bool levelUpHeld = keyboard.dKey.isPressed || keyboard.equalsKey.isPressed || keyboard.numpadPlusKey.isPressed;
		// 调等级自己一套连发计时，只受 A/D/± 影响，不掺和上面 W/S 的清零条件。
		// （两者共用一个计时器时，只按 D 会导致计时器被 W/S 的条件每帧清零 —— 见字段处注释。）
		bool anyLevelHeld = levelDownHeld || levelUpHeld;
		bool anyLevelPressed = levelDownPressed || levelUpPressed;
		if (anyLevelPressed || !anyLevelHeld)
		{
			_repeatLevelTimer = 0f;
		}
		else
		{
			_repeatLevelTimer += Time.deltaTime;
		}
		bool levelRepeatReady = _repeatLevelTimer >= RepeatInterval;
		if (levelRepeatReady)
		{
			_repeatLevelTimer -= RepeatInterval;
		}
		if (levelDownPressed || (levelDownHeld && levelRepeatReady))
		{
			AdjustLevel(-1);
		}
		if (levelUpPressed || (levelUpHeld && levelRepeatReady))
		{
			AdjustLevel(1);
		}
		// 打开面板的那一帧不响应关闭键，否则开与关会在同一帧互相抵消。
		if (Main.OpenedFrame != Time.frameCount && (keyboard.homeKey.wasPressedThisFrame || keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame))
		{
			Main.ClosePanelByUser();
			return;
		}
		bool pageDownPressed = keyboard.pageDownKey.wasPressedThisFrame;
		bool pageUpPressed = keyboard.pageUpKey.wasPressedThisFrame;
		if (pageDownPressed || pageUpPressed || keyboard.endKey.wasPressedThisFrame)
		{
			// 翻页步长 = 视口高度能放下的整行数（至少 1 行）。
			float rowStride = Mathf.Max(1f, _ui.RowH + _ui.RowSpacing);
			int pageRows = Mathf.Max(1, Mathf.FloorToInt(_ui.ViewHeight / rowStride));
			if (pageDownPressed)
			{
				_selected = Mathf.Clamp(_selected + pageRows, 0, rowCount - 1);
			}
			else if (pageUpPressed)
			{
				_selected = Mathf.Clamp(_selected - pageRows, 0, rowCount - 1);
			}
			else
			{
				_selected = rowCount - 1;
			}
			ApplySelection();
		}
	}

	/// <summary>
	/// 把当前选中行同步给主逻辑，并滚动视口保证该行可见（滚动失败不影响选中）。
	/// </summary>
	private void ApplySelection()
	{
		if (Main.Rows.Count == 0)
		{
			return;
		}
		_hasSelection = true;
		_selected = Mathf.Clamp(_selected, 0, Main.Rows.Count - 1);
		Main.SetSelected(_selected);
		try
		{
			_ui.EnsureRowVisible(_selected);
		}
		catch
		{
		}
	}

	/// <summary>
	/// 调整当前选中行的异常等级。
	/// </summary>
	/// <param name="dir">-1 降低一级，+1 提升一级。</param>
	private void AdjustLevel(int dir)
	{
		if (Main.Rows.Count != 0)
		{
			Main.AdjustRowLevel(Main.Rows[Mathf.Clamp(_selected, 0, Main.Rows.Count - 1)], dir);
		}
	}

	/// <summary>
	/// 切换当前选中行的启用 / 禁用状态。
	/// </summary>
	private void ToggleOnOff()
	{
		if (Main.Rows.Count != 0)
		{
			Main.ToggleRow(Main.Rows[Mathf.Clamp(_selected, 0, Main.Rows.Count - 1)]);
		}
	}

	/// <summary>
	/// 鼠标主处理（放在 LateUpdate，保证这一帧的 UI 布局与滚动都已结算完毕）：
	/// 拖动面板 -> 滚轮滚动 -> 悬停选中 -> 点击切换，按该优先级依次处理。
	/// </summary>
	public void LateUpdate()
	{
		Mouse mouse = GetMouse();
		if (mouse == null)
		{
			return;
		}
		try
		{
			if (_ui == null || _ui.Root == null || !_ui.Root.activeSelf)
			{
				return;
			}
			Vector2 mousePos;
			try
			{
				mousePos = ((InputControl<Vector2>)mouse.position).ReadValue();
			}
			catch
			{
				return;
			}
			Vector2 mouseDelta = mousePos - _lastMousePos;
			// 位移平方大于 4（移动超过约 2 像素）才算「悬停移动」，避免手抖导致选中行乱跳。
			bool hoverMoved = !_suppressHoverOnce && mouseDelta.sqrMagnitude > 4f;
			_suppressHoverOnce = false;
			_lastMousePos = mousePos;
			bool leftClick = mouse.leftButton.wasPressedThisFrame;
			bool rightClick = mouse.rightButton.wasPressedThisFrame;
			if (_dragging)
			{
				if (mouse.leftButton.isPressed)
				{
					Vector2 screenDelta = mousePos - _dragLastScreen;
					_dragLastScreen = mousePos;
					// 位移太小时忽略，避免静止状态下的浮点噪声反复触发面板移动。
					if (screenDelta.sqrMagnitude > 0.01f)
					{
						_ui.MovePanelBy(screenDelta, Screen.width, Screen.height);
					}
				}
				else
				{
					_dragging = false;
				}
				return;
			}
			// 在可拖动区域按下左键即进入拖动模式，直到松开左键为止。
			if (leftClick && IsDragArea(mousePos))
			{
				_dragging = true;
				_dragLastScreen = mousePos;
				return;
			}
			try
			{
				float wheelY = ((InputControl<Vector2>)mouse.scroll).ReadValue().y;
				if (Mathf.Abs(wheelY) > 0.001f)
				{
					Camera wheelCam = null;
					try
					{
						if (_canvas != null && _canvas.renderMode != 0)
						{
							wheelCam = _canvas.worldCamera;
						}
					}
					catch
					{
					}
					if (PointInRect(_ui.RootRect, mousePos, wheelCam))
					{
						// 滚轮一格约 120，夹取到 ±3 格，避免高精度滚轮一次滚过头。
						float wheelSteps = Mathf.Clamp(wheelY / 120f, -3f, 3f);
						// 滚轮向前为正、内容应下移，故取负；一次滚轮滚动 3 行的距离。
						_ui.ScrollBy((0f - wheelSteps) * (_ui.RowH + _ui.RowSpacing) * 3f);
					}
				}
			}
			catch
			{
			}
			if (!hoverMoved && !leftClick && !rightClick)
			{
				return;
			}
			Camera pickCam = null;
			try
			{
				if (_canvas != null && _canvas.renderMode != 0)
				{
					pickCam = _canvas.worldCamera;
				}
			}
			catch
			{
			}
			if (!PointInRect(_ui.RootRect, mousePos, pickCam))
			{
				return;
			}
			int hitRow = -1;
			Vector2 localPoint = default(Vector2);
			// 把鼠标屏幕坐标换算到 Content 的本地坐标，才能按 y 反推行号。
			if (_ui.Content != null && Main.Rows.Count > 0 && RectTransformUtility.ScreenPointToLocalPointInRectangle(_ui.Content, mousePos, pickCam, out localPoint))
			{
				float rowStride = Mathf.Max(1f, _ui.RowH + _ui.RowSpacing);
				// 只有横向落在内容中线两侧才算命中，避免点到内容区外的空白仍切换行。
				float contentHalfWidth = _ui.Content.rect.width * 0.5f;
				// Content 内的行自上而下排列、本地 y 为负值，取负后除以行距即得行号。
				int rowIndex = Mathf.FloorToInt((0f - localPoint.y) / rowStride);
				if (rowIndex >= 0 && rowIndex < Main.Rows.Count && Mathf.Abs(localPoint.x) <= contentHalfWidth)
				{
					hitRow = rowIndex;
				}
			}
			// 悬停切换只在鼠标确实移动过（hoverMoved）时生效，避免静止时反复重选。
			if (hoverMoved && hitRow >= 0 && (hitRow != _selected || !_hasSelection))
			{
				_selected = hitRow;
				ApplySelection();
			}
			// 左键或右键点击命中的行：先选中该行，再切换它的开关状态。
			if ((leftClick || rightClick) && hitRow >= 0)
			{
				_selected = hitRow;
				ApplySelection();
				ToggleOnOff();
			}
		}
		catch (Exception ex)
		{
			LogError("Mouse", ex.Message);
		}
	}

	/// <summary>
	/// 判断屏幕坐标是否落在可拖动区域（面板顶部标题栏或四周边缘）。
	/// </summary>
	private bool IsDragArea(Vector2 screenPoint)
	{
		if (_ui == null || _ui.RootRect == null)
		{
			return false;
		}
		try
		{
			Camera worldCam = null;
			try
			{
				if (_canvas != null && _canvas.renderMode != 0)
				{
					worldCam = _canvas.worldCamera;
				}
			}
			catch
			{
			}
			Vector2 localPoint = default(Vector2);
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_ui.RootRect, screenPoint, worldCam, out localPoint))
			{
				return false;
			}
			Rect panelRect = _ui.RootRect.rect;
			if (localPoint.x < panelRect.xMin || localPoint.x > panelRect.xMax || localPoint.y < panelRect.yMin || localPoint.y > panelRect.yMax)
			{
				return false;
			}
			// 顶部那条留白（标题栏区域）整条都可拖动。
			// 高度读 _ui.TopInset —— 这是个可变值，不能写死：
			// 面板矮时会切紧凑布局、留白从 124 缩到 72，写死的话
			// 多出来的那 52px 会被当成标题栏，玩家点那里的状态行会变成拖面板。
			if (localPoint.y >= panelRect.yMax - _ui.TopInset)
			{
				return true;
			}
			// 四周边缘同样可以拖动，方便抓住边框移动面板。
			if (localPoint.x <= panelRect.xMin + DragBorder || localPoint.x >= panelRect.xMax - DragBorder || localPoint.y <= panelRect.yMin + DragBorder || localPoint.y >= panelRect.yMax - DragBorder)
			{
				return true;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// 判断屏幕坐标是否落在指定 RectTransform 的矩形范围内（相机为空时按 Overlay 处理）。
	/// </summary>
	private static bool PointInRect(RectTransform rectTransform, Vector2 screenPoint, Camera worldCam)
	{
		if (rectTransform == null)
		{
			return false;
		}
		try
		{
			Vector2 localPoint = default(Vector2);
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, worldCam, out localPoint))
			{
				return false;
			}
			return rectTransform.rect.Contains(localPoint);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// 输出警告日志；同一条消息只在首次出现时打印，避免每帧刷屏。
	/// </summary>
	private void LogError(string tag, string message)
	{
		if (!(message == _lastError))
		{
			_lastError = message;
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] " + tag + " error: " + message);
		}
	}

	/// <summary>
	/// 把本组件注册进 Il2Cpp，供注入器在运行时挂载；重复注册会抛异常，故整体 try/catch。
	/// </summary>
	[HideFromIl2Cpp]
	public static void InitIl2Cpp()
	{
		try
		{
			ClassInjector.RegisterTypeInIl2Cpp<PanelInput>();
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] PanelInput registration failed: " + ex.Message);
		}
	}
}
