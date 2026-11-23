using Il2CppSiNiSistar2.Obj;
using UnityEngine.UI;

namespace AllAbnormalMod;

public class StatusRow
{
	internal const string RowTag = "DYJ-CBWP-YD-XL/汐蓝";

	public AbnormalType Type;

	public AbnormalData Data;

	public Text NameText;

	public Text StateText;

	public Image Bg;

	public string LastName;

	public bool LastNameBlocked;

	public int LastStateCode = int.MinValue;

	public int ModelLv = -1;

	public int LastLv = -1;

	public int LastBgLv = -1;

	public bool LastSelected;

	public bool Blocked;

	/// <summary>上一次刷新时这一行是不是「危险状态」，用来决定名字要不要染成警示色。</summary>
	public bool LastRisky;

	/// <summary>上一次刷新时这一行是不是「已点过一次、等第二次确认」。</summary>
	public bool LastArmed;
}
