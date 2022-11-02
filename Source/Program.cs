using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Zinnia.Languages.Zinnia;
using Zinnia.x86;

namespace Zinnia
{
	public static class Program
	{
		private const string PerfTestFile = @"..\..\..\..\Temp\Txt\FTest.zinnia";

		private static void MakePerfTestFile()
		{
			if (File.Exists(PerfTestFile))
				File.Delete(PerfTestFile);

			var sw = new StreamWriter(PerfTestFile);
			var lineCount = 0;
			var i = 0;

			sw.WriteLine("using System");
			sw.WriteLine();
			sw.WriteLine("const var DP1 = 7.85398125648498535156e-1");
			sw.WriteLine("const var DP2 = 3.77489470793079817668e-8");
			sw.WriteLine("const var DP3 = 2.69515142907905952645e-15");
			sw.WriteLine();
			sw.WriteLine("void Main()");
			sw.WriteLine();
		
			while (lineCount < 10000)
			{
				sw.WriteLine("public double CephesSin" + i + "(double x)");
				sw.WriteLine("	if x == 0d: return 0d");
				sw.WriteLine("	if Math.IsNaN(x) or Math.IsInfinite(x): return Math.NaN");
				sw.WriteLine();
				sw.WriteLine("	var Sign = 1");
				sw.WriteLine("	if x < 0: x = -x; Sign = -1");
				sw.WriteLine("	if x > 1.073741824e9: return 0");
				sw.WriteLine();
				sw.WriteLine("	var y = Math.Floor(x / (Math.PI / 4))");
				sw.WriteLine("	var z = Math.Floor(y / 16d)");
				sw.WriteLine("	z = y - z * 16d");
				sw.WriteLine();
				sw.WriteLine("	var j = z to int");
				sw.WriteLine("	if (j & 1) != 0: j++; y++");
				sw.WriteLine();
				sw.WriteLine("	j &= 7");
				sw.WriteLine("	if j > 3: Sign = -Sign; j -= 4");
				sw.WriteLine("	z = ((x - y * DP1) - y * DP2) - y * DP3");
				sw.WriteLine();
				sw.WriteLine("	var zz = z * z");
				sw.WriteLine("	if j == 1 or j == 2");
				sw.WriteLine("		y = -1.13585365213876817300e-11 * zz");
				sw.WriteLine("		y = (y + 2.08757008419747316778e-9) * zz");
				sw.WriteLine("		y = (y - 2.75573141792967388112e-7) * zz");
				sw.WriteLine("		y = (y + 2.48015872888517045348e-5) * zz");
				sw.WriteLine("		y = (y - 1.38888888888730564116e-3) * zz");
				sw.WriteLine("		y = (y + 4.16666666666665929218e-2)");
				sw.WriteLine("		y = 1d - zz / 2d + zz * zz * y");
				sw.WriteLine("	else ");
				sw.WriteLine("		y = 1.58962301576546568060e-10 * zz");
				sw.WriteLine("		y = (y - 2.50507477628578072866e-8) * zz");
				sw.WriteLine("		y = (y + 2.75573136213857245213e-6) * zz");
				sw.WriteLine("		y = (y - 1.98412698295895385996e-4) * zz");
				sw.WriteLine("		y = (y + 8.33333333332211858878e-3) * zz");
				sw.WriteLine("		y = (y - 1.66666666666666307295e-1)");
				sw.WriteLine("		y = z + z * zz * y");
				sw.WriteLine();
				sw.WriteLine("	return if Sign < 0: -y else y");
				sw.WriteLine();
				sw.WriteLine("public int DecodeInt" + i + "(fun byte() GetByte)");
				sw.WriteLine("	var Result = 0");
				sw.WriteLine("	var Shift = 0b");
				sw.WriteLine("	var Byte = 0b");
				sw.WriteLine();
				sw.WriteLine("	cycle Byte = GetByte()");
				sw.WriteLine("		  Result |= (Byte & $7F) << Shift to int");
				sw.WriteLine("		  Shift += 7");
				sw.WriteLine("		  if Byte & $80 == 0: break");
				sw.WriteLine();
				sw.WriteLine("	if Shift < sizeof(int) * 8 and (Byte & $40) != 0");
				sw.WriteLine("		Result |= -1 << Shift");
				sw.WriteLine();
				sw.WriteLine("	return Result");
				sw.WriteLine();
		
				lineCount += 52;
				i++;
			}

			sw.Close();
		}

		public static void PerfTest()
		{
			MakePerfTestFile();
			var arch = new x86Architecture();
			var lang = new ZinniaLanguage();
			var state = new CompilerState(null, arch, lang);
			state.SetOutput(new MemoryStream(), new MemoryStream());
			state.Entry = "Main";
			state.Format = ImageFormat.MSCoff;

			var times = 10;
			var files = new[] { PerfTestFile };
			var lines = ZinniaBuilder.ReadLines(files, state.TabSize);
			var assemblies = new List<AssemblyPath>
			{
				new("ZinniaCore", true)
			};

			Console.WriteLine("Start");
			var average = Environment.TickCount;
			var minimum = int.MaxValue;
			for (var i = 0; i < times; i++)
			{
				var ms = Environment.TickCount;
				state.CodeOut.Seek(0, SeekOrigin.Begin);
				state.LibOut.Seek(0, SeekOrigin.Begin);

				if (state.Compile(lines, assemblies))
				{
					Console.WriteLine(i + ": " + state.Strings["CompilingSucceded"]);
				}
				else
				{
					Console.WriteLine(i + ": " + state.Strings["CompilingFailed"]);
					state.Messages.WriteToConsole();
					break;
				}

				ms = Environment.TickCount - ms;
				if (ms < minimum) minimum = ms;
			}

			average = Environment.TickCount - average;
			Console.WriteLine("Minimum: {0}", minimum);
			Console.WriteLine("Average: {0}", average / times);
			state.DisposeOutput();
		}

		private static bool BuildAndRun(string[] args) 
			=> new ZinniaBuilder().BuildAndRun(args);

		public static void Main(string[] args)
		{
			//PerfTest();
			//return;

			for (var i = 0; i < 1; i++)
			{
				if (args.Length > 1) 
					BuildAndRun(args);
				else 
					Console.WriteLine("No command line parameters");
			}
		}
	}
}