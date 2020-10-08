using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Higher_Order_Functions
{
	class Program
	{
		static int GetInt(int x)
		{
			return x + 1;
		}

		static int Test(Func<int, int> Func)
		{
			return Func(2);
		}

		static void Main(string[] args)
		{
			var Time = Environment.TickCount;
			var Sum = 0;

			for (var i = 0; i < 100000000; i++)
				Sum += Test(GetInt);
		
			Time = Environment.TickCount - Time;
			Console.WriteLine("Time: " + Time + " ms");
			Console.WriteLine("Sum: " + Sum);
		}
	}
}
