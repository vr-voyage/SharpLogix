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

    class NodeDefinition
    {
        protected string typename;
        protected string[] inputs;
        protected string[] outputs;
        protected string default_output_name;

    }

    class BinaryOperationNode : NodeDefinition
    {
        public BinaryOperationNode(string name)
        {
            typename = name;
            inputs   = new string[] { "A", "B" };
            outputs = new string[] { "*" };
            default_output_name = "*";
        }
        
    }

    struct Node
    {
        public string typename;
    }
    struct NodeRef
    {
        int nodeId;
    }
    struct NodeRefGroup
    {
        List<NodeRef> nodes;
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
        List<Node> nodes;
        List<List<int>> currentOperationNodes;
        Dictionary<string, NodeRef> locals;
        Dictionary<string, NodeRef> globals;

        Dictionary<TypeCode, string> literalLogixNodes;
        Dictionary<SyntaxKind, string> binaryOperationsNodes;

        public int AddNode(string typename, string name)
        {
            int newID = nodes.Count;
            Node node = new Node
            {
                typename = typename
            };
            nodes.Add(node);

            Emit($"NODE {newID} '{typename}' \"{name}\"");

            if (currentOperationNodes.Count > 0)
            {
                 currentOperationNodes[currentOperationNodes.Count-1].Add(newID);
            }
                
            return newID;
        }

        public void AddToCurrentOperation(int id)
        {
            int currentOperandsListIndex = currentOperationNodes.Count - 1;
            if (currentOperandsListIndex < 0)
            {
                return;
            }

            currentOperationNodes[currentOperandsListIndex].Add(id);
        }

        public SharpenedSyntaxWalker()
        {
            nodes = new List<Node>();
            locals = new Dictionary<string, NodeRef>();
            globals = new Dictionary<string, NodeRef>();
            binaryOperationsNodes = new Dictionary<SyntaxKind, string>();
            currentOperationNodes = new List<List<int>>();

            literalLogixNodes = new Dictionary<TypeCode, string>();
            literalLogixNodes.Add(TypeCode.Boolean, "BooleanInput");
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

            binaryOperationsNodes.Add(SyntaxKind.AddExpression, "AddOperator");
            binaryOperationsNodes.Add(SyntaxKind.SubtractExpression, "SubtractOperator");
            binaryOperationsNodes.Add(SyntaxKind.MultiplyExpression, "MultiplyOperator");
            binaryOperationsNodes.Add(SyntaxKind.DivideExpression, "DivideOperator");
            binaryOperationsNodes.Add(SyntaxKind.BitwiseAndExpression, "BitwiseAndOperator");
            
        }

        private void Emit(string scriptLine)
        {
            Console.WriteLine(scriptLine);
        }

        private int DefineLiteral(Type type, object value)
        {
            int nodeID = -1;
            Type valueType = value.GetType();
            if (literalLogixNodes.TryGetValue(Type.GetTypeCode(valueType), out string logixInputType))
            {
                nodeID = AddNode(logixInputType, $"Literal {valueType.Name}");
                Emit($"SETCONST {nodeID} {value}");
            }
            else
            {
                Console.WriteLine($"Cannot {type} literals yet");
            }
            return nodeID;
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
            if (binaryOperationsNodes.TryGetValue(node.Kind(), out string logixBinaryOperatorType))
            {
                int listIndex = currentOperationNodes.Count;
                currentOperationNodes.Add(new List<int>(2));
                base.VisitBinaryExpression(node);
                List<int> operands = currentOperationNodes[listIndex];
                if (operands.Count < 2)
                {
                    Console.Error.WriteLine("Could not convert the operands :C");
                    return;
                }

                int nodeID = AddNode(logixBinaryOperatorType, node.Kind().ToString());
                /* FIXME Get the right Output name ! */
                Emit($"INPUT {nodeID} 'A' {operands[0]} '*'");
                Emit($"INPUT {nodeID} 'B' {operands[1]} '*'");
                
            }
                
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
                    int c = 3 + 5;
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
