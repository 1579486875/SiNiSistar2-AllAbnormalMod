using UnityEngine;
using UnityEngine.UI;

namespace AllAbnormalMod;

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

	public float MaxScroll
	{
		get
		{
			float num = ContentHeight;
			float num2 = (float)((ContentTotal > 0) ? ContentTotal : Main.Rows.Count) * (RowH + RowSpacing);
			if (num <= 0.01f)
			{
				num = num2;
			}
			return Mathf.Max(0f, num - ViewHeight);
		}
	}

	public void MovePanelBy(Vector2 screenDelta, float canvasW, float canvasH)
	{
		if (RootRect == null)
		{
			return;
		}
		try
		{
			Vector2 anchoredPosition = RootRect.anchoredPosition + screenDelta;
			Rect rect = RootRect.rect;
			anchoredPosition.x = Mathf.Clamp(anchoredPosition.x, rect.width - canvasW, 0f);
			anchoredPosition.y = Mathf.Clamp(anchoredPosition.y, rect.height * 0.5f - canvasH * 0.5f, canvasH * 0.5f - rect.height * 0.5f);
			RootRect.anchoredPosition = anchoredPosition;
		}
		catch
		{
		}
	}

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

	public void ApplyScroll()
	{
		try
		{
			if (Content != null)
			{
				Content.anchoredPosition = new Vector2(0f, ScrollY);
			}
		}
		catch
		{
		}
		RowsDirty = true;
		UpdateScrollInfo();
		UpdateScrollBar();
	}

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
			float num = Mathf.Clamp(height * (viewHeight / contentHeight), Mathf.Min(12f, height), height);
			float num2 = Mathf.Max(0.001f, contentHeight - viewHeight);
			float num3 = Mathf.Clamp01(ScrollY / num2) * (height - num);
			ScrollBar.offsetMin = new Vector2(0f, 0f - (num + num3));
			ScrollBar.offsetMax = new Vector2(0f, 0f - num3);
		}
		catch
		{
		}
	}

	public void UpdateScrollInfo()
	{
		if (ScrollInfo == null)
		{
			return;
		}
		try
		{
			int num = ((ContentTotal > 0) ? ContentTotal : Main.Rows.Count);
			if (num <= 0)
			{
				if (_lastScrollText != "0/0")
				{
					_lastScrollText = "0/0";
					ScrollInfo.text = "0/0";
				}
				return;
			}
			float num2 = RowH + RowSpacing;
			if (!(num2 <= 0.01f))
			{
				int min = Mathf.Clamp(Mathf.FloorToInt(ScrollY / num2) + 1, 1, num);
				int num3 = Mathf.Clamp(Mathf.FloorToInt((ScrollY + ViewHeight - 0.001f) / num2) + 1, min, num);
				string text = min + "-" + num3 + " / " + num;
				if (text != _lastScrollText)
				{
					_lastScrollText = text;
					ScrollInfo.text = text;
				}
			}
		}
		catch
		{
		}
	}

	public void ScrollBy(float delta)
	{
		ScrollY = Mathf.Clamp(ScrollY + delta, 0f, MaxScroll);
		ApplyScroll();
	}

	public void EnsureRowVisible(int index)
	{
		if (!(Content == null) && !(Viewport == null) && index >= 0)
		{
			float num = RowH + RowSpacing;
			float num2 = (float)index * num;
			float num3 = num2 + RowH;
			float viewHeight = ViewHeight;
			if (num2 < ScrollY)
			{
				ScrollY = num2;
			}
			else if (num3 > ScrollY + viewHeight)
			{
				ScrollY = num3 - viewHeight;
			}
			ScrollY = Mathf.Clamp(ScrollY, 0f, MaxScroll);
			ApplyScroll();
		}
	}
}
