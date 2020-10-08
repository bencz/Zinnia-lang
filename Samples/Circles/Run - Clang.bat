@echo Compiling: Circles.cpp
@clang++ Circles.cpp -O3 -c -o Circles.o
@..\..\Binaries\Zinnia.exe -x ..\CppMain.zinnia Circles.o