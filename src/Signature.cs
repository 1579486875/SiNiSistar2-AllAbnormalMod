using System.Text;

namespace AllAbnormalMod;

internal static class Signature
{
	internal const string A1 = "大赢经";

	internal const string A2 = "直插白皮";

	internal const string A3 = "赢道&";

	internal const string A4 = "汐蓝";

	internal const string Full = "大赢经直插白皮赢道&汐蓝";

	internal const string Tag = "DYJ-CBWP-YD-XL";

	internal const string Banner = "[Signature] 大赢经直插白皮赢道&汐蓝 [DYJ-CBWP-YD-XL]";

	internal const string Credit = "by 大赢经直插白皮赢道&汐蓝";

	internal const string LogPrefix = "[AllAbnormalMod][DYJ-CBWP-YD-XL] ";

	private static readonly string[] Parts = new string[4] { "大赢经", "直插白皮", "赢道&", "汐蓝" };

	private static string _assembled;

	internal static string Assembled
	{
		get
		{
			if (_assembled == null)
			{
				StringBuilder stringBuilder = new StringBuilder(24);
				for (int i = 0; i < Parts.Length; i++)
				{
					stringBuilder.Append(Parts[i]);
				}
				_assembled = stringBuilder.ToString();
			}
			return _assembled;
		}
	}
}
