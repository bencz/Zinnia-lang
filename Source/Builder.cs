using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace Zinnia
{
	public enum AssemblyFormat
	{
		Unknown,
		Application,
		Archive,
		Object,
	}

	public enum ZinniaDirectory
	{
		Binaries,
		Archives,
		Samples,
		Libraries,
	}

	public struct IncBinReference
	{
		public string Name;
		public string File;

		public IncBinReference(string Name, string File)
		{
			this.Name = Name;
			this.File = File;
		}
	}

	public class ZinniaBuilder
	{
		public IArchitecture Arch;
		public Language Language;
		public CompilerState State;
		public AssemblyFormat Format;
		public bool ExecuteApp = false;
		public bool DefaultLibs = true;

		public string Dir;
		public string ZinniaLib;
		public string Entry;
		public string OutFile;

		public string ObjFile;
		public string AsmFile;
		public string OutDir;
		public List<string> ObjectFiles;
		public List<string> Archives;
		public List<string> ZinniaFiles;
		public List<AssemblyPath> Assemblies;
		public List<IncBinReference> IncBins;

		public string ArchivesDir;
		public string BinariesDir;
		public string SamplesDir;
		public string LibrariesDir;
		public string Archiver;
		public string Linker;
		public string Assembler;

		public Dictionary<string, string> Macroes;

		public string ProcessString(string String)
		{
			foreach (var e in Macroes)
			{
				if (String.Contains(e.Key))
					String = String.Replace(e.Key, e.Value);
			}

			return String;
		}

		public static string ParentDirectory(string Dir, int Count)
		{
			var First = true;
			for (var i = Dir.Length - 1; i >= 0; i--)
			{
				if (Count == 0)
					return Dir.Substring(0, i + 1);

				var Chr = Dir[i];
				if (Chr == Path.DirectorySeparatorChar)
				{
					if (First)
						continue;

					Count--;
				}
				else if (First)
				{
					First = false;
				}
			}

			return Dir;
		}

		public static string GetDirectory(ZinniaDirectory Dir)
		{
			string Name;
			if (Dir == ZinniaDirectory.Archives) Name = "Archives";
			else if (Dir == ZinniaDirectory.Binaries) Name = "Binaries";
			else if (Dir == ZinniaDirectory.Samples) Name = "Samples";
			else if (Dir == ZinniaDirectory.Libraries) Name = "Libraries";
			else throw new ApplicationException();

			var AppDir = System.Reflection.Assembly.GetExecutingAssembly().Location;
			AppDir = Path.GetDirectoryName(AppDir);

			var Ret = Path.Combine(ParentDirectory(AppDir, 1), Name);
			if (Directory.Exists(Ret)) return Ret;

			Ret = Path.Combine(ParentDirectory(AppDir, 3), Name);
			return Directory.Exists(Ret) ? Ret : null;
		}

		public ZinniaBuilder()
		{
			ArchivesDir = GetDirectory(ZinniaDirectory.Archives);
			BinariesDir = GetDirectory(ZinniaDirectory.Binaries);
			SamplesDir = GetDirectory(ZinniaDirectory.Samples);
			LibrariesDir = GetDirectory(ZinniaDirectory.Libraries);

			Archiver = Path.Combine(BinariesDir, "ar.exe");
			Linker = Path.Combine(BinariesDir, "ld.exe");
			Assembler = Path.Combine(BinariesDir, "fasm.exe");

			Macroes = new Dictionary<string, string>()
			{
				{"$(ZinniaArchiveDir)" , ArchivesDir},
				{"$(ZinniaBinariesDir)" , BinariesDir},
				{"$(ZinniaSamplesDir)" , SamplesDir},
				{"$(ZinniaLibrariesDir)" , LibrariesDir},
			};
		}

		static string GetString(string[] Args, int i)
		{
			if (i >= Args.Length)
			{
				Console.WriteLine("Command line error");
				return null;
			}

			var Ret = Args[i];
			Args[i] = null;

			if (Ret.Length > 1 && Ret.StartsWith("\"") && Ret.EndsWith("\""))
				return Ret.Substring(1, Ret.Length - 1);

			return Ret;
		}

		public void Reset()
		{
			ObjectFiles = new List<string>();
			Archives = new List<string>();
			ZinniaFiles = new List<string>();
			Assemblies = new List<AssemblyPath>();
			IncBins = new List<IncBinReference>();

			Dir = ZinniaLib = Entry = OutFile = null;
			Arch = null;
			Language = null;
			State = null;
			Format = AssemblyFormat.Unknown;
		}

		public bool ProcessArgs(string[] Args)
		{
			ExecuteApp = false;

			for (var i = 0; i < Args.Length; i++)
				if (Args[i] != null && Args[i].Length > 1 && (Args[i][0] == '-' || Args[i][0] == '/'))
				{
					var Str = Args[i].Substring(1);
					Args[i] = null;

					if (Str[0] == 'l')
					{
						Assemblies.Add(new AssemblyPath(Str.Substring(1), true));
					}
					else if (Str == "incbin")
					{
						if (i + 2 >= Args.Length || Args[i + 2][0] == '-')
						{
							var File = GetString(Args, i + 1);
							if (File == null) return false;

							var Name = Path.GetFileNameWithoutExtension(File);
							IncBins.Add(new IncBinReference(Name, File));
						}
						else
						{
							var Name = GetString(Args, i + 1);
							if (Name == null) return false;

							var File = GetString(Args, i + 2);
							if (File == null)  return false;

							IncBins.Add(new IncBinReference(Name, File));
						}
					}
					else if (Str == "nodefaultlib")
					{
						DefaultLibs = false;
					}
					else if (Str == "x")
					{
						ExecuteApp = true;
					}
					else if (Str == "zlib")
					{
						if ((ZinniaLib = GetString(Args, i + 1)) == null)
							return false;
					}
					else if (Str == "entry")
					{
						if ((Entry = GetString(Args, i + 1)) == null)
							return false;
					}
					else if (Str == "out")
					{
						if ((OutFile = GetString(Args, i + 1)) == null)
							return false;
					}
					else if (Str == "dir")
					{
						if ((Dir = GetString(Args, i + 1)) == null)
							return false;
					}
					else if (Str == "x86")
					{
						Arch = new x86.x86Architecture();
					}
					else if (Str == "x86_64")
					{
						Arch = new x86.x86Architecture(x86.x86Extensions.Default64);
					}
					else if (Str == "zinnialang")
					{
						Language = new Languages.Zinnia.ZinniaLanguage();
					}/*
					else if (Str == "cslang")
					{
						Language = new Languages.CSharp.CSharpLanguage();
					}*/
					else if (Str == "format")
					{
						if (Format != AssemblyFormat.Unknown)
						{
							Console.WriteLine("Format is specified multiple");
							return false;
						}

						var S = GetString(Args, i + 1);
						if (S == null) return false;
						else if (S == "app") Format = AssemblyFormat.Application;
						else if (S == "arc") Format = AssemblyFormat.Archive;
						else if (S == "obj") Format = AssemblyFormat.Object;

						if (Format == AssemblyFormat.Unknown)
						{
							Console.WriteLine("Unknown assembly format: " + S);
							return false;
						}
					}
					else
					{
						Console.WriteLine("Unknown argument: " + Str);
						return false;
					}
				}

			return AdjustSettings();
		}

		public bool AdjustSettings()
		{
			if (Language == null) Language = new Languages.Zinnia.ZinniaLanguage();
			if (Arch == null) Arch = new x86.x86Architecture();
			if (Format == AssemblyFormat.Unknown) Format = AssemblyFormat.Application;
			if (Format == AssemblyFormat.Application && Entry == null) Entry = "Main";

			if (DefaultLibs)
			{
				Assemblies.Add(new AssemblyPath("ZinniaCore", true));
				Assemblies.Add(new AssemblyPath("BlitzMax", true));
			}

			if (Dir == null)
			{
				if (OutFile == null) Dir = Directory.GetCurrentDirectory();
				else Dir = Path.GetDirectoryName(Path.GetFullPath(OutFile));
			}

			if (State == null)
			{
				State = new CompilerState(this, Arch, Language);
			}
			else
			{
				State.Messages.Messages.Clear();
				State.Arch = Arch;
				State.Language = Language;
			}

			State.Entry = Entry;
			State.Format = ImageFormat.MSCoff;

			OutDir = Path.Combine(Dir, ".zinnia");
			if (!Directory.Exists(OutDir)) Directory.CreateDirectory(OutDir);

			if (OutFile == null)
			{
				if (Format == AssemblyFormat.Object)
					OutFile = Path.Combine(OutDir, "Assembly.o");
				else if (Format == AssemblyFormat.Archive)
					OutFile = Path.Combine(OutDir, "Assembly.a");
				else if (Format == AssemblyFormat.Application)
					OutFile = Path.Combine(OutDir, "Assembly.exe");
				else throw new ApplicationException();
			}

			if (ZinniaLib == null) ZinniaLib = Path.Combine(OutDir, "Assembly.zlib");
			if (AsmFile == null) AsmFile = Path.Combine(OutDir, "Assembly.s");

			if (ObjFile == null)
			{
				if (Format == AssemblyFormat.Object) ObjFile = OutFile;
				else ObjFile = Path.Combine(OutDir, "Assembly.o");
			}

			return true;
		}

		public static CodeFile[] ReadLines(IEnumerable<string> Files, int TabSize)
		{
			var Result = new List<CodeFile>();
			foreach (var e in Files)
				Result.Add(new CodeFile(e, File.ReadAllText(e), TabSize));

			return Result.ToArray();
		}

		public static bool Run(string File, string Args, bool Hide = false, bool FailIfAppFails = true)
		{
			var SInfo = new ProcessStartInfo(File, Args);
			SInfo.CreateNoWindow = Hide;
			SInfo.UseShellExecute = false;

			try
			{
				var Proc = Process.Start(SInfo);
				Proc.WaitForExit();
				return !FailIfAppFails || Proc.ExitCode == 0;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public bool CreateAssembly(CodeFile[] CodeFiles)
		{
			var RetValue = true;
			State.SetOutput(AsmFile, ZinniaLib);
			State.AssemblyName = Path.GetFileNameWithoutExtension(OutFile);

			if (State.Compile(CodeFiles, Assemblies, IncBins))
			{
				Console.WriteLine(State.Strings["CompilingSucceded"]);
				State.Messages.WriteToConsole();
			}
			else
			{
				Console.WriteLine(State.Strings["CompilingFailed"]);
				State.Messages.WriteToConsole();
				RetValue = false;
			}

			State.DisposeOutput();
			return RetValue;
		}

		public string GetFilePath(string File, string FileDir)
		{
			File = ProcessString(File);
			if (!Path.IsPathRooted(File))
				File = Path.Combine(FileDir, File);

			return File;
		}

		bool ProcessFileList(string File)
		{
			var FileDir = Path.GetDirectoryName(Path.GetFullPath(File));
			foreach (var Line in System.IO.File.ReadAllLines(File))
			{
				var NewLine = GetFilePath(Line, FileDir);
				if (!ProcessFile(NewLine)) return false;
			}

			return true;
		}

		bool ProcessFile(string File)
		{
			if (!System.IO.File.Exists(File))
			{
				Console.WriteLine("File not exists: \"" + File + "\"");
				return false;
			}

			var Ext = Path.GetExtension(File);
			if (Ext == ".txt")
			{
				if (!ProcessFileList(File))
					return false;
			}
			else if (Ext == ".zinnia" || Ext == ".cs")
			{
				ZinniaFiles.Add(File);
			}
			else if (Ext == ".zlib")
			{
				Assemblies.Add(new AssemblyPath(File));
			}
			else if (Ext == ".a" || Ext == ".lib")
			{
				Archives.Add(File);
			}
			else if (Ext == ".o" || Ext == ".obj")
			{
				ObjectFiles.Add(File);
			}
			else if (Ext == ".c")
			{
				Console.WriteLine("Compiling: " + File);
				var o = Path.Combine(OutDir, Path.GetFileName(File) + ".o");
				if (!Run("gcc", "-msse3 -mfpmath=sse -O3 -c \"" + File + "\" -o \"" + o + "\""))
				{
					Console.WriteLine("Failed to compile " + File);
					return false;
				}

				ObjectFiles.Add(o);
			}
			else if (Ext == ".cpp")
			{
				Console.WriteLine("Compiling: " + File);
				var o = Path.Combine(OutDir, Path.GetFileName(File) + ".o");
				if (!Run("g++", "-msse3 -mfpmath=sse -O3 -std=c++0x -c \"" + File + "\" -o \"" + o + "\""))
				{
					Console.WriteLine("Failed to compile " + File);
					return false;
				}

				ObjectFiles.Add(o);
			}
			else if (Ext == ".s" || Ext == ".asm")
			{
				Console.WriteLine("Assembling: " + File);
				var o = Path.Combine(OutDir, Path.GetFileName(File) + ".o");
				if (!Run(Assembler, "\"" + File + "\" \"" + o + "\""))
				{
					Console.WriteLine("Failed to assemble " + File);
					return false;
				}

				ObjectFiles.Add(o);
			}
			else
			{
				Console.WriteLine("Unknown extension: " + Ext);
				return false;
			}

			return true;
		}

		public bool Compile(string[] Args)
		{
			var RetValue = true;
			foreach (var Arg in Args)
			{
				if (Arg != null && !ProcessFile(ProcessString(Arg)))
					RetValue = false;
			}

			if (!RetValue)
				return false;

			Console.WriteLine("Compiling: Zinnia files");
			if (!CreateAssembly(ReadLines(ZinniaFiles, State.TabSize)))
				return false;

			if (ObjFile != null)
			{
				if (!Run(Assembler, "\"" + AsmFile + "\" \"" + ObjFile + "\""))
				{
					Console.WriteLine("Failed to assemble \"" + AsmFile + "\"");
					return false;
				}

				ObjectFiles.Add(ObjFile);
			}

			return true;
		}

		public bool LinkApp()
		{
			Console.WriteLine("Linking: " + OutFile);
			var Script = Path.Combine(OutDir, "Link.txt");
			var Writer = new StreamWriter(Script, false, Encoding.GetEncoding("iso-8859-1"));
			Writer.WriteLine("INPUT(");
			Writer.WriteLine("\"crtbegin.o\"");
			Writer.WriteLine("\"crt2.o\"");

			foreach (var e in ObjectFiles)
				Writer.WriteLine("\"" + Path.GetFullPath(e) + "\"");

			foreach (var e in Archives)
				Writer.WriteLine("\"" + Path.GetFullPath(e) + "\"");

			Writer.WriteLine("-lgdi32 -lwsock32 -lwinmm -ladvapi32 -lstdc++ -lmingwex -lmingw32 -lgcc -lmoldname");
			Writer.WriteLine("-lmsvcrt -luser32 -lkernel32 -lshell32 -lcomctl32 -lcomdlg32 -lglu32 -lopengl32");
			Writer.WriteLine("\"crtend.o\"");
			Writer.WriteLine(")");
			Writer.Flush();
			Writer.Dispose();

			var ArchivePath = GetDirectory(ZinniaDirectory.Archives);
			var Args = "-L" + ArchivePath + " -s -stack 4194304 -subsystem console --enable-stdcall-fixup";
			Args += " -o \"" + OutFile + "\" \"" + Script + "\"";

			if (!Run(Linker, Args))
			{
				Console.WriteLine("Failed to link \"" + OutFile + "\"");
				return false;
			}

			return true;
		}


		public bool Archive()
		{
			Console.WriteLine("Archiving: " + OutFile);

			var Args = "-c -r \"" + OutFile + "\"";
			foreach (var e in ObjectFiles)
			{
				if (Args.Length + e.Length + 1 > 1000)
				{
					if (!Run(Archiver, Args))
					{
						Console.WriteLine("Failed to archive " + OutFile);
						return false;
					}

					Args = "-c -r \"" + OutFile + "\"";
				}

				Args += " \"" + e + "\"";
			}

			if (!Run(Archiver, Args))
			{
				Console.WriteLine("Failed to archive " + OutFile);
				return false;
			}

			return true;
		}

		public bool CreateOutput()
		{
			if (Format != AssemblyFormat.Object)
			{
				if (File.Exists(OutFile))
					File.Delete(OutFile);

				if (Format == AssemblyFormat.Application)
				{ if (!LinkApp()) return false; }
				else if (Format == AssemblyFormat.Archive)
				{ if (!Archive()) return false; }
				else
					throw new NotImplementedException();
			}

			return true;
		}

		public bool Execute()
		{
			Console.WriteLine("Executing: " + Path.GetFileName(OutFile));
			return Run(OutFile, "", FailIfAppFails: false);
		}

		public bool BuildAndRun(string[] Args)
		{
			Reset();
			Args = Args.ToArray();
			if (!ProcessArgs(Args)) return false;
			if (!Compile(Args)) return false;
			if (!CreateOutput()) return false;
			if (ExecuteApp) return Execute();
			return true;
		}
	}
}
