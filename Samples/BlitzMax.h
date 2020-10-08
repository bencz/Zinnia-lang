#ifndef BMXLIBS_H
#define BMXLIBS_H

typedef void* INT_PTR;

enum KEY_CODES
{
	KEY_BACKSPACE = 8, KEY_TAB,
	KEY_ENTER = 13,
	KEY_ESCAPE = 27,
	KEY_SPACE = 32,
	KEY_PAGEUP = 33, KEY_PAGEDOWN, KEY_END, KEY_HOME,
	KEY_LEFT = 37, KEY_UP, KEY_RIGHT, KEY_DOWN,
	KEY_INSERT = 45, KEY_DELETE,
	KEY_N0 = 48, KEY_N1, KEY_N2, KEY_N3, KEY_N4, KEY_N5, KEY_N6, KEY_N7, KEY_N8, KEY_N9,
	KEY_A = 65, KEY_B, KEY_C, KEY_D, KEY_E, KEY_F, KEY_G, KEY_H, KEY_I, KEY_J,
	KEY_K, KEY_L, KEY_M, KEY_N, KEY_O, KEY_P, KEY_Q, KEY_R, KEY_S, KEY_T,
	KEY_U, KEY_V, KEY_W, KEY_X, KEY_Y, KEY_Z,
	
	KEY_LSYS = 91, KEY_RYS,
	
	KEY_NUMPAD0 = 96, KEY_NUMPAD1, KEY_NUMPAD2, KEY_NUMPAD3, KEY_NUMPAD4,
	KEY_NUMPAD5, KEY_NUMPAD6, KEY_NUMPAD7, KEY_NUMPAD8, KEY_NUMPAD9,
	KEY_NUMPADMUL = 106, KEY_NUMPADADD, KEY_NUMPADSLASH,
	KEY_NUMPADSUB, KEY_NUMPADDECIMAL, KEY_NUMPADDIVIDE,

	KEY_F1 = 112, KEY_F2, KEY_F3, KEY_F4, KEY_F5, KEY_F6,
	KEY_F7, KEY_F8, KEY_F9, KEY_F10, KEY_F11, KEY_F12,

	KEY_LSHIFT = 160, KEY_RSHIFT,
	KEY_LCONTROL = 162, KEY_RCONTROL,
	KEY_LALT = 164, KEY_RALT,

	KEY_TILDE = 192, KEY_MINUS = 189, KEY_EQUALS = 187,
	KEY_OPENBACKET = 219, KEY_CLOSEBRACKET = 221, KEY_BACKSLASH = 226,
	KEY_SEMICOLON = 186, KEY_QUTES = 222,
	KEY_COMMA = 188, KEY_PERIOD = 190, KEY_SLASH = 19,
};

enum BLEND_MODE
{
    BLEND_MODE_MASK = 1,
    BLEND_MODE_SOLID,
    BLEND_MODE_ALPHA,
    BLEND_MODE_LIGHT,
    BLEND_MODE_SHARE,
};
   
enum PIXEL_FORMAT
{
    PIXEL_FORMAT_A8,
    PIXEL_FORMAT_I8,
    PIXEL_FORMAT_RGB888,
    PIXEL_FORMAT_BGR888,
    PIXEL_FORMAT_RGBA8888,
    PIXEL_FORMAT_BGRA8888,
};

extern "C"
{
	void			Main();
	void            bb_BeginPerf();
	void            bb_EndPerf();
    void            bbGCCollect();
    int             bbMilliSecs();
	void 			bb_FunctionCallTest(int x, int y, int z, int w);
    
	INT_PTR         brl_graphics_Graphics(int Width, int Height, int Depth = 0, int Hertz = 60, int Flags = 0);
	void            brl_graphics_Flip(int Sync);

	bool            brl_polledinput_AppTerminate();
	bool            brl_polledinput_KeyHit(KEY_CODES key);
	int             brl_polledinput_MouseX();
	int             brl_polledinput_MouseY();
	
    INT_PTR         brl_max2d_CreateImage(int Width, int Height, int Frames = 1, int Flags = -1);
    INT_PTR         brl_max2d_LockImage(INT_PTR Image, int Frame = 0, bool Read = true, bool Write = true);
    void            brl_max2d_UnlockImage(INT_PTR Image, int Frame = 0);
    int             brl_max2d_ImageWidth(INT_PTR Image);
    int             brl_max2d_ImageHeight(INT_PTR Image);
    
	void            brl_max2d_Cls();
    void            bb_DrawFrameStats();
	void            brl_max2d_DrawRect(float x, float y, float Width, float Height);
	void            brl_max2d_SetColor(int Red, int Green, int Blue);
	void            brl_max2d_SetBlend(BLEND_MODE Mode);
	void            brl_max2d_SetAlpha(float Alpha);
    void            brl_max2d_DrawImage(INT_PTR Image, float X, float Y, int Frame = 0);
    void            brl_max2d_DrawImageRect(INT_PTR Image, float X, float Y, float Width, float Height, int Frame = 0);
	void 			brl_max2d_DrawPixmap(INT_PTR Pixmap, int X, int Y);
	
    INT_PTR         brl_pixmap_CreatePixmap(int Width, int Height, PIXEL_FORMAT Format, int Align);
    INT_PTR         brl_pixmap_CopyPixmap(INT_PTR Pixmap);
    INT_PTR         brl_pixmap_ConvertPixmap(INT_PTR Pixmap, PIXEL_FORMAT Format);
    int             brl_pixmap_PixmapWidth(INT_PTR Pixmap);
    int             brl_pixmap_PixmapHeight(INT_PTR Pixmap);
    int             brl_pixmap_PixmapPitch(INT_PTR Pixmap);
    PIXEL_FORMAT    brl_pixmap_PixmapFormat(INT_PTR Pixmap);
    void*           brl_pixmap_PixmapPixelPtr(INT_PTR Pixmap, int X, int Y);
    INT_PTR         brl_pixmap_PixmapWindow(INT_PTR Pixmap, int X, int Y, int Width, int Height);
    INT_PTR         brl_pixmap_ResizePixmap(INT_PTR Pixmap, int Width, int Height);
    void            brl_pixmap_ClearPixels(INT_PTR Pixmap, int Color);
    void            brl_pixmap_WritePixel(INT_PTR Pixmap, int X, int Y, int Color);
    int             brl_pixmap_ReadPixel(INT_PTR Pixmap, int X, int Y);
}

#ifndef SKIP_DEFINES
#define BeginPerf bb_BeginPerf
#define EndPerf bb_EndPerf
#define PrintInt bb_PrintInt
#define PrintLong bb_PrintLong
#define PrintDouble bb_PrintDouble
#define NewLine bb_NewLine

#define GCCollect bbGCCollect
#define MilliSecs bbMilliSecs

#define CreatePixmap brl_pixmap_CreatePixmap
#define CopyPixmap brl_pixmap_CopyPixmap
#define ConvertPixmap brl_pixmap_ConvertPixmap
#define PixmapWidth brl_pixmap_PixmapWidth
#define PixmapHeight brl_pixmap_PixmapHeight
#define PixmapPitch brl_pixmap_PixmapPitch
#define PixmapFormat brl_pixmap_PixmapFormat
#define PixmapPixelPtr brl_pixmap_PixmapPixelPtr
#define PixmapWindow brl_pixmap_PixmapWindow
#define ResizePixmap brl_pixmap_ResizePixmap
#define ClearPixels brl_pixmap_ClearPixels
#define WritePixel brl_pixmap_WritePixel
#define ReadPixel brl_pixmap_ReadPixel

#define Graphics brl_graphics_Graphics
#define Flip brl_graphics_Flip

#define AppTerminate brl_polledinput_AppTerminate
#define KeyHit brl_polledinput_KeyHit
#define MouseX brl_polledinput_MouseX
#define MouseY brl_polledinput_MouseY

#define CreateImage brl_max2d_CreateImage
#define LockImage brl_max2d_LockImage
#define UnlockImage brl_max2d_UnlockImage
#define ImageWidth brl_max2d_ImageWidth
#define ImageHeight brl_max2d_ImageHeight

#define Cls brl_max2d_Cls
#define DrawFrameStats bb_DrawFrameStats
#define DrawRect brl_max2d_DrawRect
#define SetColor brl_max2d_SetColor
#define SetBlend brl_max2d_SetBlend
#define SetAlpha brl_max2d_SetAlpha
#define DrawImage brl_max2d_DrawImage
#define DrawImageRect brl_max2d_DrawImageRect
#define DrawPixmap brl_max2d_DrawPixmap
#endif

#endif