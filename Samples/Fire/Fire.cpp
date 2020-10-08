#include <stdio.h>
#include <stdlib.h>
#include <math.h>
#include <memory.h>
#include "..\BlitzMax.h"

#define Width 560
#define Height 420
#define ScreenWidth 1024
#define ScreenHeight 768
#define RectSize 80
#define ColorNumber 768

#define MAX(a,b) ((a) > (b) ? a : b)
#define MIN(a,b) ((a) < (b) ? a : b)

typedef unsigned char byte;

float Array[Width][Height];
int Colors[ColorNumber];

void Initialize();
void Update();
float GetColor(int x, int y);
int ArgbColor(byte a, byte r, byte g, byte b);
double GetDistance(double x1, double y1, double x2, double y2);
byte Clamp(int x);
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

float GetColor(int x, int y)
{
    if (x >= 0 && x < Width && y >= 0 && y < Height)
        return Array[x][y];
        
    return 0;
}

void UpdateValues()
{
    for (int x = 0; x < Width; x++)
    {
        for (int y = 0; y < Height; y++)
        {
            float Value = Array[x][y] * 5;
            Value += GetColor(x + 1, y + 1) * 0.5;
            Value += GetColor(x - 1, y + 1) * 0.75;
            Value += GetColor(x - 2, y + 2) * 0.5;
            Value += GetColor(x - 3, y + 3) * 0.25;
            Array[x][y] = Value / 7.1;
        }
    }
    
    float MouseLeft = (float)MouseX() * Width / ScreenWidth;
    float MouseTop = (float)MouseY() * Height / ScreenHeight;
    
    int RectLeft = (int)MouseLeft - RectSize;
    int RectRight = (int)MouseLeft + RectSize;
    int RectTop = (int)MouseTop - RectSize;
    int RectButtom = (int)MouseTop + RectSize;
    
    if (RectLeft < 0) RectLeft = 0;
    if (RectRight >= Width) RectRight = Width - 1;
    if (RectTop < 0) RectTop = 0;
    if (RectButtom >= Height) RectButtom = Height - 1;
    
    for (int x = RectLeft; x <= RectRight; x++)
    {
        for (int y = RectTop; y <= RectButtom; y++)
        {
            float Dist = 1 - GetDistance(MouseLeft, MouseTop, x, y) / RectSize;
            if (Dist >= 0.0f) Array[x][y] = MIN(Array[x][y] + Dist / 10.0f, 1);
        }
    }
}
    
void UpdateImage(INT_PTR Image)
{
    INT_PTR Pixmap = LockImage(Image);
    for (int x = 0; x < Width; x++)
    {
        for (int y = 0; y < Height; y++)
        {
            int Color = Colors[(int)(Array[x][y] * (ColorNumber - 1))];
            WritePixel(Pixmap, x, y, Color);
        }
    }
    
    UnlockImage(Image);
}

void Initialize()
{
	memset(Array, 0, sizeof(Array));
	
    for (int i = 0; i < ColorNumber; i++)
    {
        float Value = (float)i / (ColorNumber - 1);
        int Red = (int)((MIN(Value, 1.0 / 3)) * 3 * 255);
        int Green = (int)((MIN(Value, 1.65 / 3) - 0.65 / 3) * 3 * 255);
        int Blue = (int)((MIN(Value, 1.0) - 2.0 / 3) * 3 * 255);
        Colors[i] = ArgbColor(255, Clamp(Red), Clamp(Green), Clamp(Blue));
    }
}
 
void Main()
{
    Initialize();
    Graphics(ScreenWidth, ScreenHeight);
    
    INT_PTR Image = CreateImage(Width, Height);
    while (!KeyHit(KEY_ESCAPE) && !AppTerminate())
    {
        Cls();
        UpdateValues();
        UpdateImage(Image);
        DrawImageRect(Image, 0, 0, ScreenWidth, ScreenHeight);
        DrawFrameStats();
        Flip(-1);
    }
}