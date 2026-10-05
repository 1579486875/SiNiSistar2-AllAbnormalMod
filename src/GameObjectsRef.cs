using UnityEngine;
using UnityEngine.UI;

namespace AllAbnormalMod;

/// <summary>
/// 面板 UI 元素引用集合：保存构建期创建的各个控件引用，并提供滚动、显隐、拖动与页码刷新等辅助方法。
/// 注意：这些公开字段与方法被 PanelInput 等其它文件直接引用，属于对外契约，不要改名。
/// </summary>
public class GameObjectsRef
{
	internal const string BuildTag = "DYJ-CBWP-YD-XL";

	public GameObject Root;

	public RectTransform RootRect;

	public RectTransform Viewport;

	public RectTransform Content;

	public Text Title;

	public Text ScrollInfo;

	public Text ActiveCountText;

	public Text HintText;

	public RectTransform ScrollTrack;

	public RectTransform ScrollBar;

	public float RowH = 34f;

	public float RowSpacing = 2f;

	public float ScrollY;

	public int ContentTotal;

	public bool RowsDirty;

	private string _lastScrollText;

	/// <summary>
	/// 内容容器（Content）的实际高度；布局尚未完成时返回 0。
	/// </summary>
	public float ContentHeight
	{
		get
		{
			try
			{
				if (Content == null)
				{
					return 0f;
				}
				return Content.rect.height;
			}
			catch
			{
				return 0f;
			}
		}
	}

	/// <summary>
	/// 视口（Viewport）的可见高度；取不到时返回 0。
	/// </summary>
	public float ViewHeight
	{
		get
		{
			try
			{
				if (Viewport == null)
				{
					return 0f;
				}
				return Viewport.rect.height;
			}
			catch
			{
				return 0f;
			}
		}
	}

	/// <summary>
	/// 允许的最大滚动量 = 内容高度 - 视口高度（不小于 0）。
	/// 内容高度尚未算出时退化为「行数 x 行距」的估算值，保证面板刚显示时也能滚动。
	/// </summary>
	public float MaxScroll
	{
		get
		{
			float measuredHeight = ContentHeight;
			float estimatedHeight = (float)((ContentTotal > 0) ? ContentTotal : Main.Rows.Count) * (RowH + RowSpacing);
			// 内容高度未知时的兜底值：行数 x 行距。
			// 布局尚未完成时 Content.rect.height 为 0，改用按行数估算的高度。
			if (measuredHeight <= 0.01f)
			{
				measuredHeight = estimatedHeight;
			}
			// 内容比视口还短时没有可滚动空间，负值收口为 0。
			return Mathf.Max(0f, measuredHeight - ViewHeight);
		}
	}

	/// <summary>
	/// 按屏幕像素位移移动面板，并把面板位置夹取在画布范围内，防止拖出屏幕。
	/// </summary>
	public void MovePanelBy(Vector2 screenDelta, float canvasW, float canvasH)
	{
		if (RootRect == null)
		{
			return;
		}
		try
		{
			Vector2 anchoredPosition = RootRect.anchoredPosition + screenDelta;
			Rect panelRect = RootRect.rect;
			// 水平方向：面板右边缘不能越过画布左边缘（width - canvasW 是负的下限）。
			anchoredPosition.x = Mathf.Clamp(anchoredPosition.x, panelRect.width - canvasW, 0f);
			// 垂直方向：以面板中心对齐画布中心为基准，按半高对称夹取。
			anchoredPosition.y = Mathf.Clamp(anchoredPosition.y, panelRect.height * 0.5f - canvasH * 0.5f, canvasH * 0.5f - panelRect.height * 0.5f);
			RootRect.anchoredPosition = anchoredPosition;
		}
		catch
		{
		}
	}

	/// <summary>
	/// 显示面板，并立即把累积的滚动量套用一次（隐藏期间它可能已经变化）。
	/// </summary>
	public void Show()
	{
		try
		{
			if (Root != null)
			{
				Root.SetActive(value: true);
				ApplyScroll();
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// 隐藏面板（由自动关闭逻辑调用，与用户手动关闭相区别）。
	/// </summary>
	public void HideAuto()
	{
		try
		{
			if (Root != null)
			{
				Root.SetActive(value: false);
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// 把 ScrollY 套用到内容容器，并刷新滚动信息文本与滚动条；调用后标记行位置需要重算。
	/// </summary>
	public void ApplyScroll()
	{
		try
		{
			if (Content != null)
			{
				// 统一用 ScrollY 表达滚动位置：值越大表示内容被推得越高（即看到越靠后的行）。
				Content.anchoredPosition = new Vector2(0f, ScrollY);
			}
		}
		catch
		{
		}
		// 滚动后行位置需要重算，同时刷新页码文本与滚动条。
		RowsDirty = true;
		UpdateScrollInfo();
		UpdateScrollBar();
	}

	/// <summary>
	/// 按 视口高度 / 内容高度 的比例刷新滚动条滑块的长度与位置；内容不足一屏时隐藏滑块。
	/// </summary>
	public void UpdateScrollBar()
	{
		if (ScrollBar == null || ScrollTrack == null)
		{
			return;
		}
		try
		{
			float height = ScrollTrack.rect.height;
			float contentHeight = ContentHeight;
			float viewHeight = ViewHeight;
			// 轨道过矮或内容不足一屏时滚动条没有意义，直接隐藏。
			if (height <= 1f || contentHeight <= viewHeight + 1f)
			{
				if (ScrollBar.gameObject.activeSelf)
				{
					ScrollBar.gameObject.SetActive(value: false);
				}
				return;
			}
			if (!ScrollBar.gameObject.activeSelf)
			{
				ScrollBar.gameObject.SetActive(value: true);
			}
			// 滑块长度按 视口/内容 比例换算，下限 12 像素、上限为轨道高度。
			float thumbHeight = Mathf.Clamp(height * (viewHeight / contentHeight), Mathf.Min(12f, height), height);
			// 可滚动范围（内容高度 - 视口高度）；下限取 0.001 是为了防止下面除零。
			float scrollRange = Mathf.Max(0.001f, contentHeight - viewHeight);
			// 滑块距轨道顶部的偏移 = 滚动比例 x 剩余可滑动距离。
			float thumbOffset = Mathf.Clamp01(ScrollY / scrollRange) * (height - thumbHeight);
			// offset 以 y 向上为正，这里用负值把滑块从轨道顶部往下摆放。
			ScrollBar.offsetMin = new Vector2(0f, 0f - (thumbHeight + thumbOffset));
			ScrollBar.offsetMax = new Vector2(0f, 0f - thumbOffset);
		}
		catch
		{
		}
	}

	/// <summary>
	/// 刷新「起始行-结束行 / 总行数」提示文本；文本没有变化时不写回，避免无谓的 UI 重建。
	/// </summary>
	public void UpdateScrollInfo()
	{
		if (ScrollInfo == null)
		{
			return;
		}
		try
		{
			// 总行数优先用缓存的 ContentTotal，尚未同步时退回真实行数。
			int rowCount = ((ContentTotal > 0) ? ContentTotal : Main.Rows.Count);
			if (rowCount <= 0)
			{
				// 文本没变就不写回，减少 UI 重建开销。
				if (_lastScrollText != "0/0")
				{
					_lastScrollText = "0/0";
					ScrollInfo.text = "0/0";
				}
				return;
			}
			float rowStride = RowH + RowSpacing;
			if (!(rowStride <= 0.01f))
			{
				// 起始行号 = 滚动位置对应行 + 1（显示给人看，从 1 开始）。
				int firstRow = Mathf.Clamp(Mathf.FloorToInt(ScrollY / rowStride) + 1, 1, rowCount);
				// 结束行号：视口底部先回退 0.001 像素再取整，避免恰好对齐时多算一行。
				int lastRow = Mathf.Clamp(Mathf.FloorToInt((ScrollY + ViewHeight - 0.001f) / rowStride) + 1, firstRow, rowCount);
				string infoText = firstRow + "-" + lastRow + " / " + rowCount;
				// 与上次的文本比较后再赋值，避免每帧触发 UI 重建。
				if (infoText != _lastScrollText)
				{
					_lastScrollText = infoText;
					ScrollInfo.text = infoText;
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// 按给定像素量滚动列表（自动夹取到 [0, MaxScroll]）。
	/// </summary>
	public void ScrollBy(float delta)
	{
		ScrollY = Mathf.Clamp(ScrollY + delta, 0f, MaxScroll);
		ApplyScroll();
	}

	/// <summary>
	/// 保证指定行完整落在视口内：行在视口上方就把视口顶对齐该行，在下方则把视口底对齐该行底部。
	/// </summary>
	public void EnsureRowVisible(int index)
	{
		if (!(Content == null) && !(Viewport == null) && index >= 0)
		{
			// 行距 = 行高 + 行间距。
			float rowStride = RowH + RowSpacing;
			// 该行顶边在内容坐标系中的位置。
			float rowTop = (float)index * rowStride;
			// 该行底边位置 = 顶边 + 行高。
			float rowBottom = rowTop + RowH;
			float viewHeight = ViewHeight;
			// 行落在视口上方：把视口顶对齐到该行。
			if (rowTop < ScrollY)
			{
				ScrollY = rowTop;
			}
			// 行落在视口下方：把视口底对齐到该行底部。
			else if (rowBottom > ScrollY + viewHeight)
			{
				ScrollY = rowBottom - viewHeight;
			}
			// 夹取到合法滚动范围。
			ScrollY = Mathf.Clamp(ScrollY, 0f, MaxScroll);
			ApplyScroll();
		}
	}
}
