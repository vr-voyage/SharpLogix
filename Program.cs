using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
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
        public NodeRef(int id)
        {
            nodeId = id;
        }
    }
    struct NodeRefGroup
    {
        List<NodeRef> nodes;
    }
    class ActiveElement
    {
        NodeRef node;
        string defaultOutputName;
        string selectedOutput;
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
        System.Numerics.Vector2 nodePosition;

        List<OperationNodes> currentOperationNodes;
        Dictionary<string, NodeRef> locals;
        Dictionary<string, NodeRef> globals;

        NodeRef undefined;

        enum IdentifierKind
        {
            INVALID,
            Local,
            Global,
            Namespace,
            Method,
            Field
        }

        struct CurrentIdentifierPart {
            IdentifierKind lastIdentifierKind;
            string name;
        }

        List<CurrentIdentifierPart> currentIdentifier;

        Dictionary<TypeCode, string> literalLogixNodes;
        Dictionary<SyntaxKind, string> binaryOperationsNodes;

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        public SharpenedSyntaxWalker()
        {
            undefined = new NodeRef
            {
                nodeId = -1
            };
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

            binaryOperationsNodes.Add(SyntaxKind.AddExpression,        "Add_Int");
            binaryOperationsNodes.Add(SyntaxKind.SubtractExpression,   "Sub_Int");
            binaryOperationsNodes.Add(SyntaxKind.MultiplyExpression,   "Mul_Float");
            binaryOperationsNodes.Add(SyntaxKind.DivideExpression,     "Div_Int");
            binaryOperationsNodes.Add(SyntaxKind.BitwiseAndExpression, "AND_Bool");

            script = new List<string>(512);
            string programTitle = Base64Encode("Test program");
            Emit($"PROGRAM \"{programTitle}\" 2");

            nodePosition.X = 0;
            nodePosition.Y = 0;
        }

        public void PositionNextBottom()
        {
            nodePosition.Y += 75;
        }

        public void PositionNextForward()
        {
            nodePosition.X += 150;
            nodePosition.Y = 0;
            
        }

        private void Emit(string scriptLine)
        {
            script.Add(scriptLine);
            //Console.WriteLine(scriptLine);
        }

        private void EmitPosition(int nodeID)
        {
            Emit($"POS {nodeID} {((int)nodePosition.X)} {(int)nodePosition.Y}");
        }

        public int AddNode(string typename, string name)
        {
            int newID = nodes.Count;
            Node node = new Node
            {
                typename = typename
            };
            nodes.Add(node);

            Emit($"NODE {newID} 'FrooxEngine.LogiX.{typename}' \"{Base64Encode($"Node {newID} {name}")}\"");
            EmitPosition(newID);
            PositionNextBottom();

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






        public string GetScript()
        {
            return String.Join("\n", script) + "\n";
        }

        private int DefineLiteral(Type type, object value)
        {
            int nodeID = -1;
            Type valueType = value.GetType();
            if (literalLogixNodes.TryGetValue(Type.GetTypeCode(valueType), out string logixInputType))
            {
                string logixType = "Input." + logixInputType;
                string valueContent = value.ToString();
                /*if (logixInputType == "FloatInput")
                {
                    valueContent = valueContent.TrimEnd(new char[] { ' ', 'f' });
                }*/
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
            PositionNextForward();
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
            string varName = node.Identifier.ToString();

            locals.Add(varName, undefined);
            /* Undefined variable... We'll catch up at the definition */
            if (node.Initializer == null) return;

            CollectionPush();
            base.VisitVariableDeclarator(node);
            OperationNodes logixNodes = CollectionPop();

            int nodeID = logixNodes[logixNodes.Count - 1];
            NodeRef nodeRef = new NodeRef();
            nodeRef.nodeId = nodeID;
            locals[varName] = nodeRef;
            Console.WriteLine($"VAR {node.Identifier} {nodeID}");


        }


        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            /* FIXME : This is just a quick hack for demonstration purposes.
             * Find a real design...
             */
            string nodeClass;
            string[] inputs;
            string functionName = node.Expression.ToString();
            switch (functionName)
            {
                case "Color.FromHSV":
                    {
                        nodeClass = "Color.HSV_ToColor";
                        inputs = new string[] { "H", "S", "V" };

                    }
                    break;
                case "Time.CurrentTime":
                    {
                        nodeClass = "Input.TimeNode";
                        inputs = new string[0];
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unsupported function {functionName}");
                    return;
            }

            CollectionPush();
            base.VisitInvocationExpression(node);
            OperationNodes nodes = CollectionPop();

            PositionNextForward();
            int functionNodeID = AddNode(nodeClass, functionName);

            if (nodes.Count < inputs.Length)
            {
                Console.Error.WriteLine($"Not enough arguments for function {functionName}");
            }

            for (int i = 0; i < inputs.Length; i++)
            {
                string inputName = inputs[i];
                int argumentNodeID = nodes[i];
                /* FIXME Don't expect the node default output to be '*' */
                string outputName = "*";
                Emit($"INPUT {functionNodeID} '{inputName}' {argumentNodeID} '{outputName}'");
            }


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

                string logixType = "Operators." + logixBinaryOperatorType;

                PositionNextForward();
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

        public static string NeosValuesArrayString(params string[] values)
        {
            return $"[{String.Join(";", values)}]";
        }

        public override void VisitParameter(ParameterSyntax node)
        {
            string nodeType = node.Type.ToString();
            if (nodeType == "float")
            {
                /* FIXME : Try dynamic variables */
                string paramName = node.Identifier.ToString();
                int nodeID = AddNode("Data.ValueRegister<System.Single>", $"Parameter {paramName}");
                locals.Add(paramName, new NodeRef(nodeID));
                
            }
            base.VisitParameter(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            if (node.Type.ToString() == "Color")
            {
                int nodeID = AddNode("Input.ColorTextInput", "new Color");
                var args = node.ArgumentList.Arguments;
                string r = args[0].ToString();
                string g = args[1].ToString();
                string b = args[2].ToString();
                string a = args[3].ToString();
                string color = NeosValuesArrayString(r, g, b, a);
                Emit($"SETCONST {nodeID} \"{Base64Encode(color)}\"");
            }
            else
            {
                base.VisitObjectCreationExpression(node);
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

            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

            SyntaxTree tree = CSharpSyntaxTree.ParseText(@"

                Color RandomColor(float currentTime)
                {
                    return Color.FromHSV(currentTime, 1.0f, 1.0f);
                }
                public void WonderfulMethod(float speed)
                {
                    float time = Time.CurrentTime() * speed;
                    RandomColor(time);
                }
            ");
            var walker = new SharpenedSyntaxWalker();
            walker.Visit(tree.GetRoot());

            string script = walker.GetScript();
            Console.WriteLine(script);

            string homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            File.WriteAllText(homePath + "/Documents/Neos VR/Sample.lgx", script);
            
        }
    }
}
