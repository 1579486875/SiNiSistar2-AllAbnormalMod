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
}
