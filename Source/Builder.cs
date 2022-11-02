using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using Zinnia.Languages.Zinnia;
using Zinnia.x86;

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

		public IncBinReference(string name, string file)
		{
			Name = name;
			File = file;
		}
	}

	public class ZinniaBuilder
	{
		private IArchitecture _arch;
		private Language _language;
		private CompilerState _state;
		public AssemblyFormat Format;
		private bool _executeApp = false;
		private bool _defaultLibs = true;

		private string _dir;
		private string _zinniaLib;
		private string _entry;
		private string _outFile;

		private string _objFile;
		private string _asmFile;
		private string _outDir;
		public List<string> ObjectFiles;
		public List<string> Archives;
		private List<string> _zinniaFiles;
		private List<AssemblyPath> _assemblies;
		private List<IncBinReference> _incBins;

		private string _archivesDir;
		private string _binariesDir;
		private string _samplesDir;
		private string _librariesDir;
		private string _archiver;
		private string _linker;
		private string _assembler;

		private Dictionary<string, string> _macroes;

		public string ProcessString(string str)
		{
			foreach (var e in _macroes.Where(e => str.Contains(e.Key)))
			{
				str = str.Replace(e.Key, e.Value);
			}

			return str;
		}

		public static string ParentDirectory(string dir, int count)
		{
			var first = true;
			for (var i = dir.Length - 1; i >= 0; i--)
			{
				if (count == 0)
					return dir.Substring(0, i + 1);

				var chr = dir[i];
				if (chr == Path.DirectorySeparatorChar)
				{
					if (first)
						continue;

					count--;
				}
				else if (first)
				{
					first = false;
				}
			}

			return dir;
		}

		public static string GetDirectory(ZinniaDirectory dir)
		{
			var name = dir switch
			{
				ZinniaDirectory.Archives => "Archives",
				ZinniaDirectory.Binaries => "Binaries",
				ZinniaDirectory.Samples => "Samples",
				ZinniaDirectory.Libraries => "Libraries",
				_ => throw new ApplicationException()
			};

			var appDir = System.Reflection.Assembly.GetExecutingAssembly().Location;
			appDir = Path.GetDirectoryName(appDir);

			var ret = Path.Combine(ParentDirectory(appDir, 1), name);
			if (Directory.Exists(ret))
				return ret;

			ret = Path.Combine(ParentDirectory(appDir, 3), name);
			return Directory.Exists(ret) ? ret : null;
		}

		public ZinniaBuilder()
		{
			_archivesDir = GetDirectory(ZinniaDirectory.Archives);
			_binariesDir = GetDirectory(ZinniaDirectory.Binaries);
			_samplesDir = GetDirectory(ZinniaDirectory.Samples);
			_librariesDir = GetDirectory(ZinniaDirectory.Libraries);

			_archiver = Path.Combine(_binariesDir, "ar.exe");
			_linker = Path.Combine(_binariesDir, "ld.exe");
			_assembler = Path.Combine(_binariesDir, "fasm.exe");

			_macroes = new Dictionary<string, string>()
			{
				{"$(ZinniaArchiveDir)" , _archivesDir},
				{"$(ZinniaBinariesDir)" , _binariesDir},
				{"$(ZinniaSamplesDir)" , _samplesDir},
				{"$(ZinniaLibrariesDir)" , _librariesDir},
			};
		}

		static string GetString(string[] args, int i)
		{
			if (i >= args.Length)
			{
				Console.WriteLine("Command line error");
				return null;
			}

			var ret = args[i];
			args[i] = null;

			if (ret.Length > 1 && ret.StartsWith("\"") && ret.EndsWith("\""))
				return ret.Substring(1, ret.Length - 1);

			return ret;
		}

		public void Reset()
		{
			ObjectFiles = new List<string>();
			Archives = new List<string>();
			_zinniaFiles = new List<string>();
			_assemblies = new List<AssemblyPath>();
			_incBins = new List<IncBinReference>();

			_dir = _zinniaLib = _entry = _outFile = null;
			_arch = null;
			_language = null;
			_state = null;
			Format = AssemblyFormat.Unknown;
		}

		public bool ProcessArgs(string[] args)
		{
			_executeApp = false;

			for (var i = 0; i < args.Length; i++)
				if (args[i] != null && args[i].Length > 1 && (args[i][0] == '-' || args[i][0] == '/'))
				{
					var str = args[i].Substring(1);
					args[i] = null;

					if (str[0] == 'l')
					{
						_assemblies.Add(new AssemblyPath(str.Substring(1), true));
					}
					else if (str == "incbin")
					{
						if (i + 2 >= args.Length || args[i + 2][0] == '-')
						{
							var file = GetString(args, i + 1);
							if (file == null) return false;

							var name = Path.GetFileNameWithoutExtension(file);
							_incBins.Add(new IncBinReference(name, file));
						}
						else
						{
							var name = GetString(args, i + 1);
							if (name == null) return false;

							var file = GetString(args, i + 2);
							if (file == null) return false;

							_incBins.Add(new IncBinReference(name, file));
						}
					}
					else if (str == "nodefaultlib")
					{
						_defaultLibs = false;
					}
					else if (str == "x")
					{
						_executeApp = true;
					}
					else if (str == "zlib")
					{
						if ((_zinniaLib = GetString(args, i + 1)) == null) 
							return false;
					}
					else if (str == "entry")
					{
						if ((_entry = GetString(args, i + 1)) == null) 
							return false;
					}
					else if (str == "out")
					{
						if ((_outFile = GetString(args, i + 1)) == null) 
							return false;
					}
					else if (str == "dir")
					{
						if ((_dir = GetString(args, i + 1)) == null) 
							return false;
					}
					else if (str == "x86")
					{
						_arch = new x86Architecture();
					}
					else if (str == "x86_64")
					{
						_arch = new x86Architecture(x86Extensions.Default64);
					}
					else if (str == "zinnialang")
					{
						_language = new ZinniaLanguage();
					} /*
					else if (Str == "cslang")
					{
						Language = new Languages.CSharp.CSharpLanguage();
					}*/
					else if (str == "format")
					{
						if (Format != AssemblyFormat.Unknown)
						{
							Console.WriteLine("Format is specified multiple");
							return false;
						}

						var s = GetString(args, i + 1);
						switch (s)
						{
							case null:
								return false;
							case "app":
								Format = AssemblyFormat.Application;
								break;
							case "arc":
								Format = AssemblyFormat.Archive;
								break;
							case "obj":
								Format = AssemblyFormat.Object;
								break;
						}

						if (Format == AssemblyFormat.Unknown)
						{
							Console.WriteLine("Unknown assembly format: " + s);
							return false;
						}
					}
					else
					{
						Console.WriteLine("Unknown argument: " + str);
						return false;
					}
				}

			return AdjustSettings();
		}

		public bool AdjustSettings()
		{
			_language ??= new Languages.Zinnia.ZinniaLanguage();
			_arch ??= new x86.x86Architecture();
			if (Format == AssemblyFormat.Unknown) Format = AssemblyFormat.Application;
			if (Format == AssemblyFormat.Application && _entry == null) _entry = "Main";

			if (_defaultLibs)
			{
				_assemblies.Add(new AssemblyPath("ZinniaCore", true));
				_assemblies.Add(new AssemblyPath("BlitzMax", true));
			}

			_dir ??= _outFile == null 
				? Directory.GetCurrentDirectory() 
				: Path.GetDirectoryName(Path.GetFullPath(_outFile));

			if (_state == null)
			{
				_state = new CompilerState(this, _arch, _language);
			}
			else
			{
				_state.Messages.Messages.Clear();
				_state.Arch = _arch;
				_state.Language = _language;
			}

			_state.Entry = _entry;
			_state.Format = ImageFormat.MSCoff;

			if (_dir != null) 
				_outDir = Path.Combine(_dir, ".zinnia");
			
			if (!Directory.Exists(_outDir))
				Directory.CreateDirectory(_outDir);

			_outFile ??= Format switch
			{
				AssemblyFormat.Object => Path.Combine(_outDir, "Assembly.o"),
				AssemblyFormat.Archive => Path.Combine(_outDir, "Assembly.a"),
				AssemblyFormat.Application => Path.Combine(_outDir, "Assembly.exe"),
				_ => throw new ApplicationException()
			};

			_zinniaLib ??= Path.Combine(_outDir, "Assembly.zlib");
			_asmFile ??= Path.Combine(_outDir, "Assembly.s");
			_objFile ??= Format == AssemblyFormat.Object 
				? _outFile 
				: Path.Combine(_outDir, "Assembly.o");

			return true;
		}

		public static CodeFile[] ReadLines(IEnumerable<string> files, int tabSize) 
			=> files.Select(e => new CodeFile(e, File.ReadAllText(e), tabSize)).ToArray();

		private static bool Run(string file, string args, bool hide = false, bool failIfAppFails = true)
		{
			var sInfo = new ProcessStartInfo(file, args)
			{
				CreateNoWindow = hide,
				UseShellExecute = false
			};

			try
			{
				var proc = Process.Start(sInfo);
				proc.WaitForExit();
				return !failIfAppFails || proc.ExitCode == 0;
			}
			catch (Exception)
			{
				return false;
			}
		}

		private bool CreateAssembly(CodeFile[] codeFiles)
		{
			var retValue = true;
			_state.SetOutput(_asmFile, _zinniaLib);
			_state.AssemblyName = Path.GetFileNameWithoutExtension(_outFile);

			if (_state.Compile(codeFiles, _assemblies, _incBins))
			{
				Console.WriteLine(_state.Strings["CompilingSucceded"]);
				_state.Messages.WriteToConsole();
			}
			else
			{
				Console.WriteLine(_state.Strings["CompilingFailed"]);
				_state.Messages.WriteToConsole();
				retValue = false;
			}

			_state.DisposeOutput();
			return retValue;
		}

		public string GetFilePath(string file, string fileDir)
		{
			file = ProcessString(file);
			if (!Path.IsPathRooted(file))
				file = Path.Combine(fileDir, file);

			return file;
		}

		private bool ProcessFileList(string file)
		{
			var fileDir = Path.GetDirectoryName(Path.GetFullPath(file));
			return System.IO.File.ReadAllLines(file)
				.Select(line => GetFilePath(line, fileDir))
				.All(newLine => ProcessFile(newLine));
		}

		private bool ProcessFile(string file)
		{
			if (!System.IO.File.Exists(file))
			{
				Console.WriteLine("File not exists: \"" + file + "\"");
				return false;
			}

			var ext = Path.GetExtension(file);
			if (ext == ".txt")
			{
				if (!ProcessFileList(file))
					return false;
			}
			else if (ext == ".zinnia" || ext == ".cs")
			{
				_zinniaFiles.Add(file);
			}
			else if (ext == ".zlib")
			{
				_assemblies.Add(new AssemblyPath(file));
			}
			else if (ext == ".a" || ext == ".lib")
			{
				Archives.Add(file);
			}
			else if (ext == ".o" || ext == ".obj")
			{
				ObjectFiles.Add(file);
			}
			else if (ext == ".c")
			{
				Console.WriteLine("Compiling: " + file);
				var o = Path.Combine(_outDir, Path.GetFileName(file) + ".o");
				if (!Run("gcc", "-msse3 -mfpmath=sse -O3 -c \"" + file + "\" -o \"" + o + "\""))
				{
					Console.WriteLine("Failed to compile " + file);
					return false;
				}

				ObjectFiles.Add(o);
			}
			else if (ext == ".cpp")
			{
				Console.WriteLine("Compiling: " + file);
				var o = Path.Combine(_outDir, Path.GetFileName(file) + ".o");
				if (!Run("g++", "-msse3 -mfpmath=sse -O3 -std=c++0x -c \"" + file + "\" -o \"" + o + "\""))
				{
					Console.WriteLine("Failed to compile " + file);
					return false;
				}

				ObjectFiles.Add(o);
			}
			else if (ext == ".s" || ext == ".asm")
			{
				Console.WriteLine("Assembling: " + file);
				var o = Path.Combine(_outDir, Path.GetFileName(file) + ".o");
				if (!Run(_assembler, "\"" + file + "\" \"" + o + "\""))
				{
					Console.WriteLine("Failed to assemble " + file);
					return false;
				}

				ObjectFiles.Add(o);
			}
			else
			{
				Console.WriteLine("Unknown extension: " + ext);
				return false;
			}

			return true;
		}

		private bool Compile(string[] args)
		{
			var retValue = true;
			foreach (var arg in args)
			{
				if (arg != null && !ProcessFile(ProcessString(arg)))
					retValue = false;
			}

			if (!retValue)
				return false;

			Console.WriteLine("Compiling: Zinnia files");
			if (!CreateAssembly(ReadLines(_zinniaFiles, _state.TabSize)))
				return false;

			if (_objFile != null)
			{
				if (!Run(_assembler, "\"" + _asmFile + "\" \"" + _objFile + "\""))
				{
					Console.WriteLine("Failed to assemble \"" + _asmFile + "\"");
					return false;
				}

				ObjectFiles.Add(_objFile);
			}

			return true;
		}

		private bool LinkApp()
		{
			Console.WriteLine("Linking: " + _outFile);
			var script = Path.Combine(_outDir, "Link.txt");
			var writer = new StreamWriter(script, false, Encoding.GetEncoding("iso-8859-1"));
			writer.WriteLine("INPUT(");
			writer.WriteLine("\"crtbegin.o\"");
			writer.WriteLine("\"crt2.o\"");

			foreach (var e in ObjectFiles)
				writer.WriteLine("\"" + Path.GetFullPath(e) + "\"");

			foreach (var e in Archives)
				writer.WriteLine("\"" + Path.GetFullPath(e) + "\"");

			writer.WriteLine("-lgdi32 -lwsock32 -lwinmm -ladvapi32 -lstdc++ -lmingwex -lmingw32 -lgcc -lmoldname");
			writer.WriteLine("-lmsvcrt -luser32 -lkernel32 -lshell32 -lcomctl32 -lcomdlg32 -lglu32 -lopengl32");
			writer.WriteLine("\"crtend.o\"");
			writer.WriteLine(")");
			writer.Flush();
			writer.Dispose();

			var archivePath = GetDirectory(ZinniaDirectory.Archives);
			var args = "-L" + archivePath + " -s -stack 4194304 -subsystem console --enable-stdcall-fixup";
			args += " -o \"" + _outFile + "\" \"" + script + "\"";

			if (!Run(_linker, args))
			{
				Console.WriteLine("Failed to link \"" + _outFile + "\"");
				return false;
			}

			return true;
		}


		private bool Archive()
		{
			Console.WriteLine("Archiving: " + _outFile);

			var args = "-c -r \"" + _outFile + "\"";
			foreach (var e in ObjectFiles)
			{
				if (args.Length + e.Length + 1 > 1000)
				{
					if (!Run(_archiver, args))
					{
						Console.WriteLine("Failed to archive " + _outFile);
						return false;
					}

					args = "-c -r \"" + _outFile + "\"";
				}

				args += " \"" + e + "\"";
			}

			if (!Run(_archiver, args))
			{
				Console.WriteLine("Failed to archive " + _outFile);
				return false;
			}

			return true;
		}

		private bool CreateOutput()
		{
			if (Format != AssemblyFormat.Object)
			{
				if (File.Exists(_outFile))
					File.Delete(_outFile);

				switch (Format)
				{
					case AssemblyFormat.Application:
					{
						if (!LinkApp())
							return false;
						break;
					}
					case AssemblyFormat.Archive:
					{
						if (!Archive())
							return false;
						break;
					}
					default:
						throw new NotImplementedException();
				}
			}

			return true;
		}

		private bool Execute()
		{
			Console.WriteLine("Executing: " + Path.GetFileName(_outFile));
			return Run(_outFile, "", failIfAppFails: false);
		}

		public bool BuildAndRun(string[] args)
		{
			Reset();
			args = args.ToArray();
			if (!ProcessArgs(args)) return false;
			if (!Compile(args)) return false;
			if (!CreateOutput()) return false;
			if (_executeApp) return Execute();
			return true;
		}
	}
}
