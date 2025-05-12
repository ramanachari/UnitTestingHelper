using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.Differencing;

public class MyTsGenerator
{
    public void Generate(string sourcePath, string outputPath)
    {
        var code = File.ReadAllText(sourcePath);
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetCompilationUnitRoot();

        var classNode = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classNode == null)
        {
            Console.WriteLine("❌ No class found.");
            return;
        }

        string className = classNode.Identifier.Text;
        var methods = classNode.Members.OfType<MethodDeclarationSyntax>();

        using var writer = new StreamWriter(outputPath);

        writer.WriteLine("using Microsoft.VisualStudio.TestTools.UnitTesting;");
        writer.WriteLine("using Moq;");
        writer.WriteLine("using System;");
        writer.WriteLine();
        writer.WriteLine("[TestClass]");
        writer.WriteLine($"public class {className}Tests");
        writer.WriteLine("{");


        foreach (var method in methods)
        {
            string methodName = method.Identifier.Text;
            string returnType = method.ReturnType.ToString();

            var conditions = method.DescendantNodes().OfType<IfStatementSyntax>().ToList();
            var ternaries = method.DescendantNodes().OfType<ConditionalExpressionSyntax>().ToList();
            var coalesces = method.DescendantNodes().OfType<BinaryExpressionSyntax>().Where(x => x.IsKind(SyntaxKind.CoalesceExpression)).ToList();
            var nullChecks = method.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(x => x.ToString().Contains("IsNullOrEmpty")).ToList();

            int testCounter = 1;

            // Handle all conditions
            foreach (var condition in conditions)
            {
                GenerateMethodLogic(code, className, writer, method, $"_IfBlock_{testCounter++}");
            }

            // Handle ternary (?:) operator
            foreach (var tern in ternaries)
            {
                GenerateMethodLogic(code, className, writer, method, $"_TernaryTrue_{testCounter++}");
                GenerateMethodLogic(code, className, writer, method, $"_TernaryFalse_{testCounter++}");
            }

            // Handle null coalescing (??)
            foreach (var col in coalesces)
            {
                GenerateMethodLogic(code, className, writer, method, $"_NullCoalesce_Null_{testCounter++}");
                GenerateMethodLogic(code, className, writer, method, $"_NullCoalesce_NotNull_{testCounter++}");
                
            }

            // Handle string.IsNullOrEmpty
            foreach (var nullCheck in nullChecks)
            {
                GenerateMethodLogic(code, className, writer, method, $"IsNullOrEmpty_{testCounter++}");
            }

            // Handle simple method
            if (!conditions.Any() && !ternaries.Any() && !coalesces.Any() && !nullChecks.Any())
            {
                GenerateMethodLogic(code, className, writer, method, $"_{testCounter}");
            }


            writer.WriteLine();
        }

        writer.WriteLine("}");
        Console.WriteLine("✅ Tests generated to: " + outputPath);
    }


    private void GenerateMethodLogic(string sourceCode, string className, StreamWriter writer, MethodDeclarationSyntax method, string methodCondition)
    {
        string returnType = method.ReturnType.ToString();
        string methodName = method.Identifier.Text;
        string parameters = string.Join(", ", method.ParameterList.Parameters.Select(p => p.ToString()));


        writer.WriteLine($"    [TestMethod]");
        writer.WriteLine($"    public void {methodName}_{methodCondition}()");
        writer.WriteLine("    {");

        var ctorMatch = Regex.Match(sourceCode, @$"{className}\s*\(([^)]*)\)");
        var dependencies = new List<(string Type, string Name)>();
        if (ctorMatch.Success)
        {
            var ctorParams = ctorMatch.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var param in ctorParams)
            {
                var parts = param.Trim().Split(' ');
                if (parts.Length == 2)
                    dependencies.Add((parts[0], parts[1]));
            }
        }

        // Declare mock fields
        foreach (var (Type, Name) in dependencies)
            writer.WriteLine($"        Mock<{Type}> mock{Name.Substring(1)} = new();");
        writer.WriteLine();

        var paramList = parameters.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var param in paramList)
        {
            var parts = param.Trim().Split(' ');
            if (parts.Length == 2)
            {
                string type = parts[0];
                string name = parts[1];
                string value = GetSampleValue(type);
                writer.WriteLine($"        {type} {name} = {value};");
            }
        }

        if (dependencies.Any())
        {
            var setupParams = string.Join(", ", GetParamNames(paramList));
            var mockName = $"mock{dependencies[0].Name.Substring(1)}";
            var setupParamValues = string.Join(", ", paramList.Select(p =>
            {
                var type = p.Trim().Split(' ')[0];
                return $"It.IsAny<{type}>()";
            }));

            writer.WriteLine($"        {mockName}.Setup(s => s.{methodName}({setupParamValues})).Returns(/* expected */);");

            writer.WriteLine($"        var {className.ToLower()} = new {className}({string.Join(", ", dependencies.Select(d => $"mock{d.Name.Substring(1)}.Object"))});");
        }
        else
        {
            writer.WriteLine($"        var {className.ToLower()} = new {className}();");
        }

        writer.WriteLine($"        var result = {className.ToLower()}.{methodName}({string.Join(", ", GetParamNames(paramList))});");

        if (returnType != "void")
            writer.WriteLine($"        Assert.AreEqual(/* expected */, result);");

        writer.WriteLine("    }\n");

    }
    
    static string GetSampleValue(string type) => type switch
    {
        "int" => "1",
        "double" => "1.0",
        "string" => "\"test\"",
        "bool" => "true",
        "DateTime" => "DateTime.Now",
        _ => $"default({type})"
    };

    static string[] GetParamNames(string[] paramList)
    {
        var names = new string[paramList.Length];
        for (int i = 0; i < paramList.Length; i++)
        {
            var parts = paramList[i].Trim().Split(' ');
            names[i] = parts.Length == 2 ? parts[1] : $"param{i}";
        }
        return names;
    }
}
