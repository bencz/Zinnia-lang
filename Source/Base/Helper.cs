using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Zinnia.Base;

public class ByRef<T>
{
    public T Object;

    public ByRef(T Object)
    {
        this.Object = Object;
    }
}

public struct AutoAllocatedList<T> : IEnumerable<T>
{
    private class Enumerator : IEnumerator, IEnumerator<T>
    {
        private AutoAllocatedList<T> List;
        private int Pos = -1;

        public Enumerator(AutoAllocatedList<T> List)
        {
            this.List = List;
        }

        public bool MoveNext()
        {
            Pos++;
            return Pos < List.Count;
        }

        public void Reset()
        {
            Pos = -1;
        }

        object IEnumerator.Current => List[Pos];

        T IEnumerator<T>.Current => List[Pos];

        public void Dispose()
        {
        }
    }

    public List<T> List;

    public AutoAllocatedList(List<T> List)
    {
        this.List = List;
    }

    public T this[int Index]
    {
        get
        {
            if (Index < 0 || Index >= Count)
                throw new ArgumentOutOfRangeException("Index");

            return List[Index];
        }

        set
        {
            if (Index < 0 || Index >= Count)
                throw new ArgumentOutOfRangeException("Index");

            List[Index] = value;
        }
    }

    public int Count => List == null ? 0 : List.Count;

    public void Set(List<T> List)
    {
        this.List = List;
    }

    public void Add(T Item)
    {
        Allocate();
        List.Add(Item);
    }

    public void AddRange(IEnumerable<T> Items)
    {
        Allocate();
        List.AddRange(Items);
    }

    public void AddRange(AutoAllocatedList<T> List)
    {
        if (List.List != null)
        {
            Allocate();
            this.List.AddRange(List.List);
        }
    }

    public void Allocate()
    {
        if (List == null)
            List = new List<T>();
    }

    public void ReAllocate()
    {
        List = new List<T>();
    }

    public void Insert(int Index, T Item)
    {
        Allocate();
        List.Insert(Index, Item);
    }

    public void InsertRange(int Index, IEnumerable<T> Items)
    {
        Allocate();
        List.InsertRange(Index, Items);
    }

    public void Remove(T Item)
    {
        if (List != null)
            List.Remove(Item);
    }

    public void RemoveAt(int Index)
    {
        if (Index < 0 || Index >= Count)
            throw new ArgumentOutOfRangeException("Index");

        List.RemoveAt(Index);
    }

    public void RemoveAll(Predicate<T> Func)
    {
        if (List != null)
            List.RemoveAll(Func);
    }

    public void RemoveRange(int Index, int Count)
    {
        if (Index < 0 || Index >= this.Count)
            throw new ArgumentOutOfRangeException("Index");

        if (Index + Count > this.Count)
            throw new ArgumentOutOfRangeException("Count");

        if (List != null)
            List.RemoveRange(Index, Count);
    }

    public bool Contains(T Item)
    {
        return List != null ? List.Contains(Item) : false;
    }

    public int IndexOf(T Item)
    {
        return List != null ? List.IndexOf(Item) : -1;
    }

    public static implicit operator AutoAllocatedList<T>(List<T> List)
    {
        return new AutoAllocatedList<T>(List);
    }

    public List<T> ToList()
    {
        Allocate();
        return List;
    }

    public AutoAllocatedList<T> Copy()
    {
        if (List == null) return new AutoAllocatedList<T>();
        return new AutoAllocatedList<T>(List.ToList());
    }

    public AutoAllocatedList<T2> Change<T2>() where T2 : class
    {
        var Ret = new AutoAllocatedList<T2>();
        for (var i = 0; i < Count; i++)
            Ret.Add(this[i] as T2);

        return Ret;
    }

    public void Clear()
    {
        if (List != null)
            List.Clear();
    }

    public void ForEach(Action<T> Action)
    {
        if (List != null)
            List.ForEach(Action);
    }

    public bool TrueForAll(Predicate<T> Func)
    {
        return List == null ? true : List.TrueForAll(Func);
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return new Enumerator(this);
    }
}

public class NotifiedList<T> : IList<T>
{
    private readonly Action _Changed;

    public NotifiedList(IList<T> BaseList, Action Changed)
    {
        if (BaseList == null)
            throw new ArgumentNullException("BaseList");

        if (Changed == null)
            throw new ArgumentNullException("Changed");

        this.BaseList = BaseList;
        _Changed = Changed;
    }

    public IList<T> BaseList { get; }

    public int IndexOf(T item)
    {
        return BaseList.IndexOf(item);
    }

    public void Insert(int index, T item)
    {
        BaseList.Insert(index, item);
        _Changed();
    }

    public void RemoveAt(int index)
    {
        BaseList.RemoveAt(index);
        _Changed();
    }

    public T this[int index]
    {
        get => BaseList[index];

        set
        {
            BaseList[index] = value;
            _Changed();
        }
    }

    public void Add(T item)
    {
        BaseList.Add(item);
        _Changed();
    }

    public void Clear()
    {
        BaseList.Clear();
        _Changed();
    }

    public bool Contains(T item)
    {
        return BaseList.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        BaseList.CopyTo(array, arrayIndex);
    }

    public int Count => BaseList.Count;

    public bool IsReadOnly => BaseList.IsReadOnly;

    public bool Remove(T item)
    {
        var Ret = BaseList.Remove(item);
        if (Ret) _Changed();
        return Ret;
    }

    public IEnumerator<T> GetEnumerator()
    {
        return BaseList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)BaseList).GetEnumerator();
    }
}

public class MyList<T> : IList<T>
{
    private T[] _Items;

    public MyList(int Capacity)
    {
        _Items = new T[Capacity];
    }

    public MyList()
    {
        _Items = new T[8];
    }

    public int Capacity
    {
        get => _Items.Length;

        set
        {
            if (value < Count)
                throw new ArgumentOutOfRangeException();

            var NewItems = new T[value];
            CopyTo(NewItems, 0);
            _Items = NewItems;
        }
    }

    public int Count { get; private set; }

    public int IndexOf(T item)
    {
        for (var i = 0; i < Count; i++)
            if (_Items[i].Equals(item))
                return i;

        return -1;
    }

    public void Insert(int index, T item)
    {
        throw new NotImplementedException();
    }

    public void RemoveAt(int index)
    {
        throw new NotImplementedException();
    }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException("index");

            return _Items[index];
        }

        set
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException("index");

            _Items[index] = value;
        }
    }

    public void Add(T item)
    {
        if (Capacity == Count)
            Capacity *= 2;

        _Items[Count] = item;
        Count++;
    }

    public void Clear()
    {
        Count = 0;
    }

    public bool Contains(T item)
    {
        return IndexOf(item) != -1;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        for (var i = 0; i < Count; i++)
            array[arrayIndex + i] = _Items[i];
    }

    public bool IsReadOnly => false;

    public bool Remove(T item)
    {
        var Index = IndexOf(item);
        if (Index == -1) return false;

        RemoveAt(Index);
        return true;
    }

    public IEnumerator<T> GetEnumerator()
    {
        throw new NotImplementedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        throw new NotImplementedException();
    }
}

public abstract class Node<T> where T : Node<T>
{
    public T Parent;

    //public abstract void ForEachChildren(Action<T> Func);

    public Node(T Parent)
    {
        this.Parent = Parent;
    }

    public T2 NodeGetParent<T2>(Predicate<T2> Func = null) where T2 : T
    {
        var Node = this;
        while (Node != null)
        {
            var TNode = Node as T2;
            if (TNode != null && (Func == null || Func(TNode)))
                return TNode;

            Node = Node.Parent;
        }

        return null;
    }

    public void ForEach(Action<T> Func)
    {
        Func(this as T);
    }
}

public struct DataList
{
    public List<object> Data;
    public object Data1, Data2, Data3;

    public bool Contains(object Obj)
    {
        if (Data1 == Obj || Data2 == Obj || Data3 == Obj)
            return true;

        return Data != null && Data.Contains(Obj);
    }

    public bool Contains<T>()
    {
        if (Data1 is T) return true;
        if (Data2 is T) return true;
        if (Data3 is T) return true;
        if (Data == null) return false;

        for (var i = 0; i < Data.Count; i++)
        {
            var Obj = (T)Data[i];
            if (Obj != null) return true;
        }

        return false;
    }

    public IEnumerable<T> Enum<T>()
    {
        if (Data1 is T) yield return (T)Data1;
        if (Data2 is T) yield return (T)Data2;
        if (Data3 is T) yield return (T)Data3;
        if (Data == null) yield break;

        for (var i = 0; i < Data.Count; i++)
        {
            var Obj = (T)Data[i];
            if (Obj != null) yield return Obj;
        }
    }

    public T Get<T>()
    {
        if (Data1 is T) return (T)Data1;
        if (Data2 is T) return (T)Data2;
        if (Data3 is T) return (T)Data3;
        if (Data == null) return default;

        for (var i = 0; i < Data.Count; i++)
        {
            var Obj = (T)Data[i];
            if (Obj != null) return Obj;
        }

        return default;
    }

    public object Get(System.Type T)
    {
        if (Data1 == null) return null;
        if (Data1.GetType().IsSubOrEqual(T)) return Data1;

        if (Data2 == null) return null;
        if (Data2.GetType().IsSubOrEqual(T)) return Data2;

        if (Data3 == null) return null;
        if (Data3.GetType().IsSubOrEqual(T)) return Data3;

        if (Data == null) return null;
        foreach (var e in Data)
            if (e.GetType().IsSubOrEqual(T))
                return e;

        return null;
    }

    public void Set(object Value, bool Overwrite = true)
    {
        var T = Value.GetType();

        if (Data1 == null)
        {
            Data1 = Value;
        }
        else if (Data1.GetType().IsSubOrEqual(T))
        {
            if (!Overwrite)
                throw new InvalidOperationException("The list already contains an object with the same type");

            Data1 = Value;
        }

        else if (Data2 == null)
        {
            Data2 = Value;
        }
        else if (Data2.GetType().IsSubOrEqual(T))
        {
            if (!Overwrite)
                throw new InvalidOperationException("The list already contains an object with the same type");

            Data2 = Value;
        }

        else if (Data3 == null)
        {
            Data3 = Value;
        }
        else if (Data3.GetType().IsSubOrEqual(T))
        {
            if (!Overwrite)
                throw new InvalidOperationException("The list already contains an object with the same type");

            Data3 = Value;
        }

        else if (Data != null)
        {
            for (var i = 0; i < Data.Count; i++)
                if (Data[i].GetType().IsSubOrEqual(T))
                {
                    if (!Overwrite)
                        throw new InvalidOperationException("The list already contains an object with the same type");

                    Data[i] = Value;
                    return;
                }

            Data.Add(Value);
        }
        else
        {
            Data = new List<object> { Value };
        }
    }

    private void Adjust(bool RemoveFromList)
    {
        if (Data != null && RemoveFromList)
            Data.RemoveAll(x => x == null);

        for (var i = 0; i < 3; i++)
            if (Data1 == null)
            {
                Data1 = Data2;
                Data2 = Data3;

                if (Data != null && Data.Count > 0)
                {
                    Data3 = Data[0];
                    Data.RemoveAt(0);
                }
            }
            else if (Data2 == null)
            {
                Data2 = Data3;
                if (Data != null && Data.Count > 0)
                {
                    Data3 = Data[0];
                    Data.RemoveAt(0);
                }
            }
            else if (Data3 == null)
            {
                if (Data != null && Data.Count > 0)
                {
                    Data3 = Data[0];
                    Data.RemoveAt(0);
                }
            }
    }

    public void Remove<T>()
    {
        Remove(typeof(T));
    }

    public void Remove(System.Type Type)
    {
        if (Data1 == null) return;
        if (Data1.GetType().IsSubOrEqual(Type)) Data1 = null;

        if (Data2 == null) return;
        if (Data2.GetType().IsSubOrEqual(Type)) Data2 = null;

        if (Data3 == null) return;
        if (Data3.GetType().IsSubOrEqual(Type)) Data3 = null;

        if (Data != null)
            Data.RemoveAll(x => x.GetType().IsSubOrEqual(Type));

        Adjust(false);
    }

    public void Clear()
    {
        Data1 = null;
        Data2 = null;
        Data3 = null;
        Data = null;
    }

    public T Create<T>(object[] Params, bool Overwrite) where T : class
    {
        var Ret = Activator.CreateInstance(typeof(T), Params);
        Set(Ret, Overwrite);
        return Ret as T;
    }

    public T Create<T>(params object[] Params) where T : class
    {
        return Create<T>(Params, true);
    }

    public T GetOrCreate<T>(params object[] Params) where T : class
    {
        var Ret = Get<T>();
        if (Ret == null) Ret = Create<T>(Params);
        return Ret;
    }

    public T GetOrCreate<T>(object Param) where T : class
    {
        var Ret = Get<T>();
        if (Ret == null)
            Ret = Create<T>(new[] { Param }, true);

        return Ret;
    }
}

public static class Helper
{
    private const int FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const int FILE_ATTRIBUTE_NORMAL = 0x80;

    public static void CopyMembers(object Dst, object Src)
    {
        var DstType = Dst.GetType();
        var SrcType = Src.GetType();
        if (!DstType.IsSubclassOf(SrcType) && !DstType.IsEquivalentTo(SrcType))
            throw new ArgumentException("Invalid types");

        var Members = SrcType.GetMembers(BindingFlags.NonPublic | BindingFlags.Public
                                                                | BindingFlags.Instance | BindingFlags.Static);

        for (var i = 0; i < Members.Length; i++)
        {
            var Member = Members[i] as FieldInfo;
            if (Member == null) continue;

            Member.SetValue(Dst, Member.GetValue(Src));
        }
    }

    public static void Foreach<T>(this T[] Array, Action<T> Func)
    {
        for (var i = 0; i < Array.Length; i++)
            Func(Array[i]);
    }

    public static bool TrueForAll<T>(this T[] Array, Predicate<T> Func)
    {
        for (var i = 0; i < Array.Length; i++)
            if (!Func(Array[i]))
                return false;

        return true;
    }

    public static bool Compare<T>(this List<T> List1, List<T> List2)
        where T : class
    {
        if (List1.Count != List2.Count) return false;

        for (var i = 0; i < List1.Count; i++)
            if (List1[i] != List2[i])
                return false;

        return true;
    }

    public static bool Compare<T>(this T[] Array1, T[] Array2)
        where T : class
    {
        if (Array1.Length != Array2.Length) return false;

        for (var i = 0; i < Array1.Length; i++)
            if (Array1[i] != Array2[i])
                return false;

        return true;
    }

    public static T[] Resize<T>(this T[] Array, int NewSize)
    {
        var MinSize = Math.Min(NewSize, Array.Length);
        var Ret = new T[NewSize];
        for (var i = 0; i < MinSize; i++)
            Ret[i] = Array[i];

        return Ret;
    }

    public static T[] Copy<T>(this T[] Array)
    {
        var Ret = new T[Array.Length];
        Array.CopyTo(Ret, 0);
        return Ret;
    }

    public static NotifiedList<T> AsNotifiedList<T>(this IList<T> BaseList, Action Changed)
    {
        return new NotifiedList<T>(BaseList, Changed);
    }

    public static int GetBracket(char c)
    {
        if (c == '(' || c == '[' || c == '{') return 1;
        if (c == ')' || c == ']' || c == '}') return -1;

        return 0;
    }

    public static bool GetBracket(char c, bool Back)
    {
        if (Back) return c == ')' || c == ']' || c == '}';
        return c == '(' || c == '[' || c == '{';
    }

    public static void ProcessNewLines(string String, int Index, int Length, Action<int> NewLine)
    {
        NewLine(0);
        ProcessLineEnds(String, Index, Length, x => NewLine(x + 1));
    }

    public static void ProcessNewLines(string String, Action<int> NewLine)
    {
        NewLine(0);
        ProcessLineEnds(String, 0, String.Length, x => NewLine(x + 1));
    }

    public static void ProcessLineEnds(string String, int Index, int Length, Action<int> EndLine)
    {
        var End = Index + Length;
        for (var i = Index; i < End; i++)
        {
            var Chr = String[i];
            if (Chr == '\n')
            {
                EndLine(i);
            }
            else if (Chr == '\r')
            {
                if (i + 1 < End && String[i + 1] == '\n')
                    i++;

                EndLine(i);
            }
        }
    }

    public static void ProcessLineEnds(string String, Action<int> EndLine)
    {
        ProcessLineEnds(String, 0, String.Length - 1, EndLine);
    }

    public static int GetLineCount(string String, int Index, int Length)
    {
        var Result = 0;
        ProcessLineEnds(String, Index, Length, x => Result++);
        return Result + 1;
    }

    public static int GetLineCount(string String)
    {
        var Result = 0;
        ProcessLineEnds(String, 0, String.Length, x => Result++);
        return Result + 1;
    }

    public static string[] GetStrings(IEnumerable<string> Array, IEnumerable<string> Skip = null)
    {
        var RetList = new List<string>();
        foreach (var e in Array)
        {
            if (Skip != null)
            {
                var Ok = true;
                foreach (var f in Skip)
                    if (f.Contains(e))
                    {
                        Ok = false;
                        break;
                    }

                if (!Ok) continue;
            }

            if (!RetList.Contains(e))
                RetList.Add(e);
        }

        if (RetList.Count == 0) return null;
        return RetList.ToArray();
    }

    public static bool IsIdChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    public static void ProcessIndexLength(int RealLength, bool Back, ref int Index, ref int Length)
    {
        if (Back)
        {
            if (Index == -1) Index = RealLength - 1;
            if (Length == -1) Length = Index + 1;

            if (Index < -1 || Index > RealLength)
                throw new ArgumentOutOfRangeException("Index");

            if (Length < 0 || Index - Length < -1)
                throw new ArgumentOutOfRangeException("Index");
        }
        else
        {
            if (Index == -1) Index = 0;
            if (Length == -1) Length = RealLength - Index;

            if (Index < -1 || Index > RealLength)
                throw new ArgumentOutOfRangeException("Index");

            if (Length < 0 || Index + Length > RealLength)
                throw new ArgumentOutOfRangeException("Index");
        }
    }

    public static void Verify(int RealLength, int Index, int Length, string IndexErr = "Index",
        string LengthErr = "Length")
    {
        if (Index < 0 || RealLength < Index)
            throw new ArgumentOutOfRangeException(IndexErr);

        if (Length < 0 || RealLength < Index + Length)
            throw new ArgumentOutOfRangeException(LengthErr);
    }

    public static void Verify(int RealLength, int Index, string IndexErr = "Index")
    {
        if (Index < 0 || RealLength < Index)
            throw new ArgumentOutOfRangeException(IndexErr);
    }

    public static string GetRelativePath(string From, string To)
    {
        var FromAttr = GetPathAttribute(From);
        var ToAttr = GetPathAttribute(To);

        var Path = new StringBuilder(260);
        if (PathRelativePathTo(Path, From, FromAttr, To, ToAttr) == 0)
            throw new ArgumentException("Paths must have a common prefix");

        return Path.ToString();
    }

    private static int GetPathAttribute(string Path)
    {
        if (Directory.Exists(Path)) return FILE_ATTRIBUTE_DIRECTORY;
        return FILE_ATTRIBUTE_NORMAL;
    }

    [DllImport("shlwapi.dll", SetLastError = true)]
    private static extern int PathRelativePathTo(StringBuilder pszPath,
        string pszFrom, int dwAttrFrom, string pszTo, int dwAttrTo);


    public static bool IsSubOrEqual(this System.Type Self, System.Type Type)
    {
        return Self.IsEquivalentTo(Type) || Self.IsSubclassOf(Type);
    }

    public static string[] GetSkipList(string[] List1, IEnumerable<string> List2)
    {
        if (List1 == null) return null;
        if (List2 == null) return List1.ToArray();

        var Ret = new List<string>();
        foreach (var e in List2)
        {
            for (var j = 0; j < List1.Length; j++)
                if (List1[j] == e)
                    goto ContinueLabel;

            for (var j = 0; j < List1.Length; j++)
                if (e.Contains(List1[j]))
                {
                    Ret.Add(e);
                    break;
                }

            ContinueLabel: ;
        }

        return ToArrayWithoutSame(Ret);
    }

    public static T[] ToArrayWithoutSame<T>(List<T> List) where T : class
    {
        RemoveSameObject(List);
        if (List.Count == 0) return null;
        return List.ToArray();
    }

    public static void RemoveSameObject<T>(List<T> List) where T : class
    {
        for (var i = 0; i < List.Count; i++)
        for (var j = i + 1; j < List.Count; j++)
            if (List[j] == List[i])
            {
                List.RemoveAt(j);
                j--;
            }
    }

    public static int Pow2Sqrt(int a)
    {
        var Val = 1;
        var Ret = 0;
        while (a > Val)
        {
            Val *= 2;
            Ret++;
        }

        return Ret;
    }

    public static BigInteger Pow2Sqrt(BigInteger a)
    {
        var Val = new BigInteger(1);
        var Ret = new BigInteger(0);
        while (a > Val)
        {
            Val *= 2;
            Ret++;
        }

        return Ret;
    }

    public static int Pow2(int a)
    {
        var Ret = 1;
        while (a > Ret) Ret *= 2;
        return Ret;
    }

    public static BigInteger Pow2(BigInteger a)
    {
        var Ret = 1U;
        while (a > Ret) Ret *= 2;
        return Ret;
    }

    public static T[] Slice<T>(this T[] Arr, int Index, int Count = -1)
    {
        if (Count == -1) Count = Arr.Length - Index;

        var Ret = new T[Count];
        for (var i = 0; i < Count; i++)
            Ret[i] = Arr[i + Index];

        return Ret;
    }

    public static List<T> Slice<T>(this List<T> Arr, int Index, int Count = -1)
    {
        if (Count == -1) Count = Arr.Count - Index;

        var Ret = new List<T>(Count);
        for (var i = 0; i < Count; i++)
            Ret.Add(Arr[i + Index]);

        return Ret;
    }

    public static IEnumerable<T> Union<T>(this IEnumerable<T> List, T New)
    {
        return List.Union(new[] { New });
    }
}