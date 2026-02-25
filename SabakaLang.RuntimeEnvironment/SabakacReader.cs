using SabakaLang.Compiler;

namespace SabakaLang.RuntimeEnvironment;

public class SabakacReader
{
    public List<Instruction> Read(byte[] bytes)
    {
        var instructions = new List<Instruction>();

        var text = System.Text.Encoding.UTF8.GetString(bytes);

        using var reader = new StringReader(text);

        string? line;
        int lineNumber = 0;

        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;

            line = line.Trim();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var opcodeText = parts[0];

            if (!Enum.TryParse<OpCode>(opcodeText, true, out var op))
                throw new Exception($"Unknown opcode '{opcodeText}' at line {lineNumber}");

            switch (op)
            {
                case OpCode.Push:
                {
                    if (parts.Length < 2)
                        throw new Exception($"Push requires operand at line {lineNumber}");

                    if (int.TryParse(parts[1], out int intVal))
                    {
                        instructions.Add(new Instruction(op, intVal));
                    }
                    else if (float.TryParse(parts[1], out float floatVal))
                    {
                        instructions.Add(new Instruction(op, floatVal));
                    }
                    else
                    {
                        throw new Exception($"Invalid Push operand at line {lineNumber}");
                    }

                    break;
                }

                default:
                    instructions.Add(new Instruction(op));
                    break;
            }
        }

        return instructions;
    }
}