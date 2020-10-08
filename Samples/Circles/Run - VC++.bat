@echo Compiling: Circles.cpp
@call "C:\Program Files (x86)\Microsoft Visual Studio 11.0\VC\vcvarsall.bat" x86
@cl.exe Circles.cpp /c -FoCircles.o -Ox
@..\..\Binaries\Zinnia.exe ..\msvcrt.lib ..\CppMain.zinnia -x Circles.o