using System.Text;

namespace SabakaLang.Studio;

public class EditorEngine
{
    public List<StringBuilder> Lines = new() { new StringBuilder() };
    public int CursorRow = 0;
    public int CursorCol = 0;

    public void InsertChar(char c)
    {
        Lines[CursorRow].Insert(CursorCol, c);
        CursorCol++;
    }
    
    public void InsertNewLine()
    {
        var remainder = Lines[CursorRow].ToString().Substring(CursorCol);
        Lines[CursorRow].Remove(CursorCol, remainder.Length);
        Lines.Insert(CursorRow + 1, new StringBuilder(remainder));
        CursorRow++;
        CursorCol = 0;
    }
    
    public void Backspace()
    {
        if (CursorCol > 0)
        {
            Lines[CursorRow].Remove(CursorCol - 1, 1);
            CursorCol--;
        }
        else if (CursorRow > 0)
        {
            CursorCol = Lines[CursorRow - 1].Length;
            Lines[CursorRow - 1].Append(Lines[CursorRow]);
            Lines.RemoveAt(CursorRow);
            CursorRow--;
        }
    }
}