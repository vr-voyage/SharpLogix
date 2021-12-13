using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpLogix
{

    public class IDGenerator
    {
        public static int id = 0;
        public static int NewID()
        {
            id++;
            return id;
        }
    }

    class Node
    {
        string nodeType;
        int nodeId;

    }
    class ActiveElement
    {
        Node node;
        string defaultOutputName;
    };

    // NumericLiteral - Int -> IntInput. 0 Inputs. 1 Output. DefaultOutputName = *
    // StringLiteral  - String -> StringInput. 0 Inputs. 1 Output. DefaultOutputName = *
    // Operator + -> Add. 2 Inputs. 1 Output. DefaultOutputName = *
    //
    // AssignmentOperation - NodeGroup. 0 Inputs. 1 Output (NodeGroup ID). DefaultOutputName = ??

    class SharpenedSyntaxWalker : CSharpSyntaxWalker
    {
        Dictionary<string, Node> locals;
        Dictionary<string, Node> globals;

        Dictionary<TypeCode, string> literalLogixNodes;

        public SharpenedSyntaxWalker()
        {
            locals = new Dictionary<string, Node>();
            globals = new Dictionary<string, Node>();

            literalLogixNodes = new Dictionary<TypeCode, string>();
            literalLogixNodes.Add(TypeCode.Boolean, "BooleaInput");
            literalLogixNodes.Add(TypeCode.Byte,    "ByteInput");
            literalLogixNodes.Add(TypeCode.SByte,   "SbyteInput");
            literalLogixNodes.Add(TypeCode.Int16,   "ShortInput");
            literalLogixNodes.Add(TypeCode.UInt16,  "UshortInput");
            literalLogixNodes.Add(TypeCode.Int32,   "IntInput");
            literalLogixNodes.Add(TypeCode.UInt32,  "UintInput");
            literalLogixNodes.Add(TypeCode.Int64,   "LongInput");
            literalLogixNodes.Add(TypeCode.UInt64,  "UlongInput");
            literalLogixNodes.Add(TypeCode.Single,  "FloatInput");
            literalLogixNodes.Add(TypeCode.Double,  "DoubleInput");
            literalLogixNodes.Add(TypeCode.Char,    "CharInput");
            literalLogixNodes.Add(TypeCode.String,  "StringInput");
            
        }

        private void Emit(string scriptLine)
        {
            Console.WriteLine(scriptLine);
        }

        private int DefineLiteral(Type type, object value)
        {
            int newID = -1;
            if (literalLogixNodes.TryGetValue(Type.GetTypeCode(value.GetType()), out string logixInputType))
            {
                newID = IDGenerator.NewID();
                Emit($"NODE {newID} {logixInputType}");
                Emit($"SETCONST {newID} {value}");
            }
            else
            {
                Console.WriteLine($"Cannot handle literals of type {type} yet");
            }
            return newID;
        }

        int tabs = 0;
        public override void Visit(SyntaxNode node)
        {
            Console.Write(new string('\t', tabs));
            Console.WriteLine(node.Kind());
            Console.Write(new string('\t', tabs));
            Console.WriteLine(node.GetText(Encoding.UTF8).ToString());
            tabs++;
            base.Visit(node);
            tabs--;
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            Console.WriteLine($"METHOD {node.Identifier} {node.ReturnType}");
            var parameters = node.ParameterList.Parameters;
            for (int i = 0; i < parameters.Count; i++)
            {
                Console.WriteLine($"PARAM {i+1} {parameters[i].Identifier}");
            }

            base.VisitMethodDeclaration(node);
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            Console.WriteLine($"Declaring a new variable named : {node.Identifier}");
            base.VisitVariableDeclarator(node);
            Console.WriteLine($"VAR {node.Identifier} {node.Initializer}");
            
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            Console.WriteLine($"Binary expression of type : {node.Kind()}");
            base.VisitBinaryExpression(node);
        }

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (node.Kind() is SyntaxKind.NumericLiteralExpression)
            {
                object val = node.Token.Value;
                DefineLiteral(val.GetType(), val);
                /*string frooxInputType = "";
                switch(Type.GetTypeCode(node.Token.Value.GetType()))
                {
                    case TypeCode.Int32:
                        frooxInputType = "IntInput";
                        break;
                    case TypeCode.Single:
                        frooxInputType = "FloatInput";
                        break;
                    case TypeCode.Double:
                        frooxInputType = "DoubleInput";
                        break;
                    default:
                        frooxInputType = "";
                        Console.WriteLine($"Don't know how to handle literal type {node.Token.Value.GetType()} in NeosVR");
                        break;

                }
                if (frooxInputType != "")
                {
                    Console.WriteLine($"NODE 999 'FrooxEngine.Logix.Input.{frooxInputType}' \"b64name_of_yetanotherliteral\"");
                    Console.WriteLine($"SETCONST 999 {node.Token.Value}");
                }*/

            }
            else
            {
                base.VisitLiteralExpression(node);
            }

            

        }


        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            Console.WriteLine($"Assigning to {node.Left} with {node.Kind()} {node.OperatorToken} and {node.Right.GetType()}");
            switch(node.Kind())
            {
                case SyntaxKind.SimpleAssignmentExpression:
                    {
                        
                    }
                    break;
                case SyntaxKind.AddAssignmentExpression:
                    {

                    }
                    break;
                case SyntaxKind.SubtractAssignmentExpression:
                    {


                    }
                    break;
                case SyntaxKind.MultiplyAssignmentExpression:
                    {
                    }
                    break;
                case SyntaxKind.DivideAssignmentExpression:
                    {

                    }
                    break;
                case SyntaxKind.ModuloAssignmentExpression:
                    {
                    }
                    break;
                case SyntaxKind.AndAssignmentExpression:
                    {

                    }
                    break;
                case SyntaxKind.OrAssignmentExpression:
                    {
                    }
                    break;
                case SyntaxKind.ExclusiveOrAssignmentExpression:
                    {
                    }
                    break;
                case SyntaxKind.LeftShiftAssignmentExpression:
                    {
                    }
                    break;
                case SyntaxKind.RightShiftAssignmentExpression:
                    {
                    }
                    break;
                case SyntaxKind.CoalesceAssignmentExpression:
                    {
                    }
                    break;
                default:
                    Console.WriteLine($"Can't manage expressions of {node.Kind()} yet");
                    break;


            }
            base.VisitAssignmentExpression(node);
        }

        

        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            
            //node.IsKind()
            base.VisitExpressionStatement(node);
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Meow IS NOT Pouip");
            SyntaxTree tree = CSharpSyntaxTree.ParseText(@"
                public int WonderfulMethod(int a, int b)
                {
                    int c = a + b;
                    c += 1;
                    c = Math.Max(15, c);
                    return c;
                }
            ");
            var walker = new SharpenedSyntaxWalker();
            walker.Visit(tree.GetRoot());
        }
    }
}
