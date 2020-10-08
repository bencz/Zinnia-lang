#include <stdio.h>
#include <stdlib.h>
#include <math.h>
#include "..\BlitzMax.h"

typedef unsigned char byte;

int ArgbColor(byte a, byte r, byte g, byte b);
double GetDistance(double x1, double y1, double x2, double y2);
byte Wrap(int x);
void UpdateImage(INT_PTR Image);

int ArgbColor(byte a, byte r, byte g, byte b)
{
    return (int)a << 24 | (int)r << 16 | (int)g << 8 | b;
}

double GetDistance(double x1, double y1, double x2, double y2)
{
    double x = x2 - x1, y = y2 - y1;
    return sqrt(x * x + y * y);
}

byte Clamp(int x)
{
    if (x < 0) x = 0;
    if (x > 255) x = 255;
    return (byte)x;
}

void UpdateImage(INT_PTR Image)
{
    INT_PTR Pixmap = LockImage(Image);
    int Width = ImageWidth(Image);
    int Height = ImageHeight(Image);
    double Time = (double)MilliSecs() / 1000;
    
    for (int x = 0; x < Width; x++)
    {
        for (int y = 0; y < Height; y++)
        {
            double RelX = (double)x / Width;
            double RelY = (double)y / Height;
            double Value = GetDistance(RelX, RelY, 0.5, 0.5) * 3 - Time;

            int Light = (int)((RelY * 100) * fabs(sin(Value / 1.5)));
            int Red = (int)((RelX * 255) * fabs(cos(Value))) + Light;
            int Green = (int)(((1 - RelY) * 255) * fabs(sin(Value))) + Light;
            int Blue = (int)((RelY * 255) * fabs(cos(Value / 3))) + Light;
            
            int Color = ArgbColor(255, Clamp(Red), Clamp(Green), Clamp(Blue));
            WritePixel(Pixmap, x, y, Color);
        }
    }
    
    UnlockImage(Image);
}

void Main()
{
    Graphics(1024, 720);
    INT_PTR Image = CreateImage(320, 240);
    
    while (!KeyHit(KEY_ESCAPE) && !AppTerminate())
    {
        Cls();
        UpdateImage(Image);
        DrawImageRect(Image, 0, 0, 1024, 720);
        DrawFrameStats();
        Flip(-1);
    }
}