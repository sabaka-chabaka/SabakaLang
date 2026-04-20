using System.Text;

namespace SabakaLang.Studio;

public class EditorEngine
{
    public List<StringBuilder> Lines = new() { new StringBuilder() };
    public int CursorRow = 0;
    public int CursorCol = 0;
    
    public int SelectionStartRow = -1;
    public int SelectionStartCol = -1;

    public bool HasSelection => SelectionStartRow != -1;

    public void ClearSelection()
    {
        SelectionStartRow = -1;
        SelectionStartCol = -1;
    }

    public void InsertChar(char c)
    {
        if (HasSelection) DeleteSelection();
        Lines[CursorRow].Insert(CursorCol, c);
        CursorCol++;
    }
    
    public void InsertNewLine()
    {
        if (HasSelection) DeleteSelection();
        var remainder = Lines[CursorRow].ToString().Substring(CursorCol);
        Lines[CursorRow].Remove(CursorCol, remainder.Length);
        Lines.Insert(CursorRow + 1, new StringBuilder(remainder));
        CursorRow++;
        CursorCol = 0;
    }
    
    public void Backspace()
    {
        if (HasSelection)
        {
            DeleteSelection();
            return;
        }

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

    public void DeleteSelection()
    {
        if (!HasSelection) return;

        var startRow = SelectionStartRow;
        var startCol = SelectionStartCol;
        var endRow = CursorRow;
        var endCol = CursorCol;

        if (startRow > endRow || (startRow == endRow && startCol > endCol))
        {
            (startRow, endRow) = (endRow, startRow);
            (startCol, endCol) = (endCol, startCol);
        }

        if (startRow == endRow)
        {
            Lines[startRow].Remove(startCol, endCol - startCol);
        }
        else
        {
            Lines[startRow].Remove(startCol, Lines[startRow].Length - startCol);
            Lines[startRow].Append(Lines[endRow].ToString().Substring(endCol));
            
            for (int i = 0; i < endRow - startRow; i++)
            {
                Lines.RemoveAt(startRow + 1);
            }
        }

        CursorRow = startRow;
        CursorCol = startCol;
        ClearSelection();
    }
}