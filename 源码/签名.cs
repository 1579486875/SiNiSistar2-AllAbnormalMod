using System.Text;

namespace AllAbnormalMod;

internal static class Signature
{
	// 下面四段是**故意**拆开的署名碎片，看起来没被引用，但不要删：
	//   1. const 是编译期常量，不进 DLL —— 它们只存在于源码里，
	//      反编译成品翻不到，只有连源码一起抄走才会带上；
	//   2. 拆成四段放在 A1~A4，是为了让人想清干净得逐处翻改，
	//      而不是删一行就完事（README「关于作者」一节有说明）；
	//   3. Assembled 属性拼出的完整署名会与这里做自检比对，
	//      被改动时日志里会打出 signature self-check mismatch。
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
