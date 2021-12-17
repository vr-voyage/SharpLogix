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
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SharpLogix
{

    


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
        public int nodeId;
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

    class OperationNodes : List<int> { }

    class SharpenedSyntaxWalker : CSharpSyntaxWalker
    {

        List<Node> nodes;
        List<string> script;
        
        List<OperationNodes> currentOperationNodes;
        Dictionary<string, NodeRef> locals;
        Dictionary<string, NodeRef> globals;

        Dictionary<TypeCode, string> literalLogixNodes;
        Dictionary<SyntaxKind, string> binaryOperationsNodes;

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        public SharpenedSyntaxWalker()
        {
            nodes = new List<Node>();
            locals = new Dictionary<string, NodeRef>();
            globals = new Dictionary<string, NodeRef>();
            binaryOperationsNodes = new Dictionary<SyntaxKind, string>();
            currentOperationNodes = new List<OperationNodes>();

            literalLogixNodes = new Dictionary<TypeCode, string>();
            literalLogixNodes.Add(TypeCode.Boolean, "BoolInput");
            literalLogixNodes.Add(TypeCode.Byte, "ByteInput");
            literalLogixNodes.Add(TypeCode.SByte, "SbyteInput");
            literalLogixNodes.Add(TypeCode.Int16, "ShortInput");
            literalLogixNodes.Add(TypeCode.UInt16, "UshortInput");
            literalLogixNodes.Add(TypeCode.Int32, "IntInput");
            literalLogixNodes.Add(TypeCode.UInt32, "UintInput");
            literalLogixNodes.Add(TypeCode.Int64, "LongInput");
            literalLogixNodes.Add(TypeCode.UInt64, "UlongInput");
            literalLogixNodes.Add(TypeCode.Single, "FloatInput");
            literalLogixNodes.Add(TypeCode.Double, "DoubleInput");
            literalLogixNodes.Add(TypeCode.Char, "CharInput");
            literalLogixNodes.Add(TypeCode.String, "StringInput");

            binaryOperationsNodes.Add(SyntaxKind.AddExpression, "Add_Int");
            binaryOperationsNodes.Add(SyntaxKind.SubtractExpression, "Sub_Int");
            binaryOperationsNodes.Add(SyntaxKind.MultiplyExpression, "Mul_Int");
            binaryOperationsNodes.Add(SyntaxKind.DivideExpression, "Div_Int");
            binaryOperationsNodes.Add(SyntaxKind.BitwiseAndExpression, "AND_Bool");

            script = new List<string>(512);
            string programTitle = Base64Encode("Test program");
            Emit($"PROGRAM \"{programTitle}\"");
        }

        public int AddNode(string typename, string name)
        {
            int newID = nodes.Count;
            Node node = new Node
            {
                typename = typename
            };
            nodes.Add(node);

            Emit($"NODE {newID} '{typename}' \"{Base64Encode($"Node {newID} {name}")}\"");

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

        int CollectionPush()
        {
            int collectionIndex = currentOperationNodes.Count;
            currentOperationNodes.Add(new OperationNodes());
            return collectionIndex;
        }

        OperationNodes invalidCollection = new OperationNodes();

        OperationNodes CollectionGetLast()
        {
            OperationNodes collection = invalidCollection;
            if (currentOperationNodes.Count > 0)
                collection = currentOperationNodes[currentOperationNodes.Count - 1];
            return collection;
        }

        OperationNodes CollectionPop()
        {
            OperationNodes poppedCollection = invalidCollection;
            int nCollections = currentOperationNodes.Count;
            if (nCollections > 0)
            {
                poppedCollection = CollectionGetLast();
                currentOperationNodes.RemoveAt(nCollections - 1);
            }

            return poppedCollection;
        }

        bool CollectionIsValid(OperationNodes collection)
        {
            return collection != invalidCollection;
        }



        private void Emit(string scriptLine)
        {
            script.Add(scriptLine);
            //Console.WriteLine(scriptLine);
        }

        public string GetScript()
        {
            return String.Join("\n", script);
        }

        private int DefineLiteral(Type type, object value)
        {
            int nodeID = -1;
            Type valueType = value.GetType();
            if (literalLogixNodes.TryGetValue(Type.GetTypeCode(valueType), out string logixInputType))
            {
                string logixType = "FrooxEngine.LogiX.Input." + logixInputType;
                nodeID = AddNode(logixType, $"Literal {valueType.Name}");
                Emit($"SETCONST {nodeID} \"{Base64Encode(value.ToString())}\"");
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

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            string identifierName = node.Identifier.ToString();
            /* FIXME : Search in every variable category */
            if (locals.ContainsKey(identifierName))
            {
                AddToCurrentOperation(locals[identifierName].nodeId);
            }
            base.VisitIdentifierName(node);
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
            CollectionPush();
            base.VisitVariableDeclarator(node);
            OperationNodes logixNodes = CollectionPop();
            int nodeID = logixNodes[logixNodes.Count - 1];
            NodeRef nodeRef = new NodeRef();
            nodeRef.nodeId = nodeID;
            locals.Add(node.Identifier.ToString(), nodeRef);
            Console.WriteLine($"VAR {node.Identifier} {nodeID}");


        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            Console.WriteLine($"Binary expression of type : {node.Kind()}");
            if (binaryOperationsNodes.TryGetValue(node.Kind(), out string logixBinaryOperatorType))
            {
                int listIndex = currentOperationNodes.Count;
                currentOperationNodes.Add(new OperationNodes());
                base.VisitBinaryExpression(node);
                List<int> operands = currentOperationNodes[listIndex];
                if (operands.Count < 2)
                {
                    Console.Error.WriteLine("Could not convert the operands :C");
                    return;
                }

                string logixType = "FrooxEngine.LogiX.Operators." + logixBinaryOperatorType;
                int nodeID = AddNode(logixType, node.Kind().ToString());
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
            }
            else
            {
                base.VisitLiteralExpression(node);
            }
        }


        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            Console.WriteLine($"Assigning to {node.Left} {node.Left.Kind()} with {node.Kind()} {node.OperatorToken} and {node.Right.GetType()}");
            if (node.Left.Kind() != SyntaxKind.IdentifierName)
            {
                Console.WriteLine("Don't know how to handle that...");
                base.VisitAssignmentExpression(node);
                return;
            }

            IdentifierNameSyntax left = (IdentifierNameSyntax)node.Left;
            string leftName = left.Identifier.ToString();
            CollectionPush();
            base.VisitAssignmentExpression(node);
            /* FIXME : Factorize */
            OperationNodes nodes = CollectionPop();
            if (nodes.Count() == 0)
            {
                Console.WriteLine("Got nothing...");
                return;
            }
            int lastID = nodes[nodes.Count - 1];

            switch (node.Kind())
            {
                case SyntaxKind.SimpleAssignmentExpression:
                    {
                        if (locals.ContainsKey(leftName))
                        {
                            NodeRef nodeRef = locals[leftName];
                            nodeRef.nodeId = lastID;
                            locals[leftName] = nodeRef;
                            Console.WriteLine($"VAR {leftName} = {lastID}");
                        }
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
                    c = c + 1;
                    c = c * 2;
                    return c;
                }
            ");
            var walker = new SharpenedSyntaxWalker();
            walker.Visit(tree.GetRoot());

            string script = walker.GetScript();
            Console.WriteLine(script);

        }
    }
}
