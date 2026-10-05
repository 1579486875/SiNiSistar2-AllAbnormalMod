using System;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AllAbnormalMod;

public class PanelInput : MonoBehaviour
{
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

	private float _repeatTimer;

	private const float RepeatInterval = 0.16f;

	private const float DragTopArea = 124f;

	private const float DragBorder = 10f;

	private void OnEnable()
	{
		_dragging = false;
		_dragLastScreen = default(Vector2);
		_suppressHoverOnce = true;
	}

	[HideFromIl2Cpp]
	public void Init(GameObjectsRef ui, Canvas canvas)
	{
		_ui = ui;
		_canvas = canvas;
		_selected = 0;
		_hasSelection = false;
	}

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

	private void OnDestroy()
	{
		_ui = null;
		_kb = null;
		_mouse = null;
	}

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

	private void HandleKeyboard()
	{
		Keyboard keyboard = GetKeyboard();
		if (keyboard == null)
		{
			return;
		}
		int count = Main.Rows.Count;
		if (count == 0)
		{
			return;
		}
		bool isPressed = keyboard.wKey.isPressed;
		bool isPressed2 = keyboard.sKey.isPressed;
		bool wasPressedThisFrame = keyboard.wKey.wasPressedThisFrame;
		bool wasPressedThisFrame2 = keyboard.sKey.wasPressedThisFrame;
		if (wasPressedThisFrame || wasPressedThisFrame2 || (!isPressed && !isPressed2))
		{
			_repeatTimer = 0f;
		}
		else
		{
			_repeatTimer += Time.deltaTime;
		}
		bool flag = _repeatTimer >= 0.16f;
		if (flag)
		{
			_repeatTimer -= 0.16f;
		}
		bool flag2 = wasPressedThisFrame || (isPressed && flag);
		bool flag3 = wasPressedThisFrame2 || (isPressed2 && flag);
		if (flag2 || flag3)
		{
			if (!_hasSelection)
			{
				float num = Mathf.Max(1f, _ui.RowH + _ui.RowSpacing);
				_selected = Mathf.Clamp(Mathf.FloorToInt(_ui.ScrollY / num), 0, count - 1);
			}
			int selected = _selected;
			_selected = Mathf.Clamp(_selected + ((!flag2) ? 1 : (-1)), 0, count - 1);
			if (_selected != selected || !_hasSelection)
			{
				ApplySelection();
			}
		}
		bool flag4 = keyboard.aKey.wasPressedThisFrame || keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame;
		bool flag5 = keyboard.aKey.isPressed || keyboard.minusKey.isPressed || keyboard.numpadMinusKey.isPressed;
		bool num2 = keyboard.dKey.wasPressedThisFrame || keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame;
		bool flag6 = keyboard.dKey.isPressed || keyboard.equalsKey.isPressed || keyboard.numpadPlusKey.isPressed;
		if (flag4 || (flag5 && flag))
		{
			AdjustLevel(-1);
		}
		if (num2 || (flag6 && flag))
		{
			AdjustLevel(1);
		}
		if (Main.OpenedFrame != Time.frameCount && (keyboard.homeKey.wasPressedThisFrame || keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame))
		{
			Main.ClosePanelByUser();
			return;
		}
		bool wasPressedThisFrame3 = keyboard.pageDownKey.wasPressedThisFrame;
		bool wasPressedThisFrame4 = keyboard.pageUpKey.wasPressedThisFrame;
		if (wasPressedThisFrame3 || wasPressedThisFrame4 || keyboard.endKey.wasPressedThisFrame)
		{
			float num3 = Mathf.Max(1f, _ui.RowH + _ui.RowSpacing);
			int num4 = Mathf.Max(1, Mathf.FloorToInt(_ui.ViewHeight / num3));
			if (wasPressedThisFrame3)
			{
				_selected = Mathf.Clamp(_selected + num4, 0, count - 1);
			}
			else if (wasPressedThisFrame4)
			{
				_selected = Mathf.Clamp(_selected - num4, 0, count - 1);
			}
			else
			{
				_selected = count - 1;
			}
			ApplySelection();
		}
	}

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

	private void AdjustLevel(int dir)
	{
		if (Main.Rows.Count != 0)
		{
			Main.AdjustRowLevel(Main.Rows[Mathf.Clamp(_selected, 0, Main.Rows.Count - 1)], dir);
		}
	}

	private void ToggleOnOff()
	{
		if (Main.Rows.Count != 0)
		{
			Main.ToggleRow(Main.Rows[Mathf.Clamp(_selected, 0, Main.Rows.Count - 1)]);
		}
	}

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
			Vector2 vector;
			try
			{
				vector = ((InputControl<Vector2>)mouse.position).ReadValue();
			}
			catch
			{
				return;
			}
			Vector2 vector2 = vector - _lastMousePos;
			bool flag = !_suppressHoverOnce && vector2.sqrMagnitude > 4f;
			_suppressHoverOnce = false;
			_lastMousePos = vector;
			bool wasPressedThisFrame = mouse.leftButton.wasPressedThisFrame;
			bool wasPressedThisFrame2 = mouse.rightButton.wasPressedThisFrame;
			if (_dragging)
			{
				if (mouse.leftButton.isPressed)
				{
					Vector2 screenDelta = vector - _dragLastScreen;
					_dragLastScreen = vector;
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
			if (wasPressedThisFrame && IsDragArea(vector))
			{
				_dragging = true;
				_dragLastScreen = vector;
				return;
			}
			try
			{
				float y = ((InputControl<Vector2>)mouse.scroll).ReadValue().y;
				if (Mathf.Abs(y) > 0.001f)
				{
					Camera cam = null;
					try
					{
						if (_canvas != null && _canvas.renderMode != 0)
						{
							cam = _canvas.worldCamera;
						}
					}
					catch
					{
					}
					if (PointInRect(_ui.RootRect, vector, cam))
					{
						float num = Mathf.Clamp(y / 120f, -3f, 3f);
						_ui.ScrollBy((0f - num) * (_ui.RowH + _ui.RowSpacing) * 3f);
					}
				}
			}
			catch
			{
			}
			if (!flag && !wasPressedThisFrame && !wasPressedThisFrame2)
			{
				return;
			}
			Camera cam2 = null;
			try
			{
				if (_canvas != null && _canvas.renderMode != 0)
				{
					cam2 = _canvas.worldCamera;
				}
			}
			catch
			{
			}
			if (!PointInRect(_ui.RootRect, vector, cam2))
			{
				return;
			}
			int num2 = -1;
			Vector2 localPoint = default(Vector2);
			if (_ui.Content != null && Main.Rows.Count > 0 && RectTransformUtility.ScreenPointToLocalPointInRectangle(_ui.Content, vector, cam2, out localPoint))
			{
				float num3 = Mathf.Max(1f, _ui.RowH + _ui.RowSpacing);
				float num4 = _ui.Content.rect.width * 0.5f;
				int num5 = Mathf.FloorToInt((0f - localPoint.y) / num3);
				if (num5 >= 0 && num5 < Main.Rows.Count && Mathf.Abs(localPoint.x) <= num4)
				{
					num2 = num5;
				}
			}
			if (flag && num2 >= 0 && (num2 != _selected || !_hasSelection))
			{
				_selected = num2;
				ApplySelection();
			}
			if ((wasPressedThisFrame || wasPressedThisFrame2) && num2 >= 0)
			{
				_selected = num2;
				ApplySelection();
				ToggleOnOff();
			}
		}
		catch (Exception ex)
		{
			LogError("Mouse", ex.Message);
		}
	}

	private bool IsDragArea(Vector2 screenPoint)
	{
		if (_ui == null || _ui.RootRect == null)
		{
			return false;
		}
		try
		{
			Camera cam = null;
			try
			{
				if (_canvas != null && _canvas.renderMode != 0)
				{
					cam = _canvas.worldCamera;
				}
			}
			catch
			{
			}
			Vector2 localPoint = default(Vector2);
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_ui.RootRect, screenPoint, cam, out localPoint))
			{
				return false;
			}
			Rect rect = _ui.RootRect.rect;
			if (localPoint.x < rect.xMin || localPoint.x > rect.xMax || localPoint.y < rect.yMin || localPoint.y > rect.yMax)
			{
				return false;
			}
			if (localPoint.y >= rect.yMax - 124f)
			{
				return true;
			}
			if (localPoint.x <= rect.xMin + 10f || localPoint.x >= rect.xMax - 10f || localPoint.y <= rect.yMin + 10f || localPoint.y >= rect.yMax - 10f)
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

	private static bool PointInRect(RectTransform rt, Vector2 screenPoint, Camera cam)
	{
		if (rt == null)
		{
			return false;
		}
		try
		{
			Vector2 localPoint = default(Vector2);
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPoint, cam, out localPoint))
			{
				return false;
			}
			return rt.rect.Contains(localPoint);
		}
		catch
		{
			return false;
		}
	}

	private void LogError(string tag, string message)
	{
		if (!(message == _lastError))
		{
			_lastError = message;
			MelonLogger.Warning("[AllAbnormalMod][DYJ-CBWP-YD-XL] " + tag + " error: " + message);
		}
	}

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
