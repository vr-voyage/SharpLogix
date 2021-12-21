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

    struct NodeInfo
    {
        public string className;
        public string defaultOutput;
        public string[] inputs;
        public string[] outputs;

        public static NodeInfo Define(
            string cName,
            string output,
            string[] nodeInputs,
            string[] nodeOutputs)
        {
            return new NodeInfo
            {
                className = cName,
                defaultOutput = output,
                inputs = nodeInputs,
                outputs = nodeOutputs
            };
        }

        public static NodeInfo Define(string cName)
        {
            return Define(cName, "*", new string[0], new string[] { "*" });
        }
    }

    class NodeDB : Dictionary<string, NodeInfo>
    {
        public NodeInfo AddInputDefinition(string name)
        {
            string[] inputs = { };
            string[] outputs = { "*" };
            return AddDefinition(name, "*", inputs, outputs);
        }

        public NodeInfo AddBinaryOperationDefinition(string name)
        {
            string[] inputs = { "A", "B" };
            string[] outputs = { "*" };
            return AddDefinition(name, "*", inputs, outputs);
        }

        public NodeInfo AddDefinition(
            string name,
            string defaultOutputName,
            string[] inputs,
            string[] outputs)
        {
            string completeName = "FrooxEngine.LogiX." + name;
            NodeInfo nodeInfo = NodeInfo.Define(
                completeName, defaultOutputName, inputs, outputs);
            this.Add(completeName, nodeInfo);
            return nodeInfo;
        }
        public NodeInfo GetNodeInformation(string name)
        {
            return this[name];
        }

        public string DefaultOutputFor(string name)
        {
            return this[name].defaultOutput;
        }

    }

    struct Node
    {
        public string typename;
        public string GenericTypeName()
        {
            return typename.Split(new char[] { '<' })[0];
        }
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

    class LogixMethodParameter
    {
        public string name;
        public string type; // FIXME : Infer this from the representing node ?
        public NodeRef node;

        public LogixMethodParameter(string paramName, string paramType, int nodeID)
        {
            name = paramName;
            type = paramType;
            node = new NodeRef(nodeID);
        }
    }

    class LogixMethod
    {
        public string name;
        public List<LogixMethodParameter> parameters;

        static readonly LogixMethodParameter invalidParam = new LogixMethodParameter("", "", -1);

        public LogixMethod(string methodName)
        {
            name = methodName;
            parameters = new List<LogixMethodParameter>(4);
        }

        public void AddParameter(string name, string type, int nodeID)
        {
            parameters.Add(new LogixMethodParameter(name, type, nodeID));
        }

        public LogixMethodParameter GetParameter(string name)
        {
            foreach (LogixMethodParameter methodParam in parameters)
            {
                if (methodParam.name == name) return methodParam;
            }
            return invalidParam;
        }
    }

    class Nodes : List<Node>
    {
        public readonly static Node invalidNode = new Node();

        public Node GetNode(int nodeID)
        {
           if (nodeID >= this.Count)
            {
                return invalidNode;
            }
            return this[nodeID];
        }
    }

    class SharpenedSyntaxWalker : CSharpSyntaxWalker
    {

        readonly NodeDB nodeDB;

        readonly Nodes nodes;
        readonly List<string> script;
        System.Numerics.Vector2 nodePosition;

        List<OperationNodes> currentOperationNodes;
        readonly Dictionary<string, NodeRef> locals;
        readonly Dictionary<string, NodeRef> globals;
        readonly Dictionary<string, LogixMethod> methods;
        string currentMethodName = "";
        int currentReturnID = -1;

        public int currentImpulseOutputNode = -1;
        public string currentImpulseOutputName = null;

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
        /* FIXME : Find a better name */
        Dictionary<string, string> typesList;


        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        public string LogixNamespacePrefix(string suffix)
        {
            return "FrooxEngine.LogiX." + suffix;
        }

        public SharpenedSyntaxWalker()
        {
            undefined = new NodeRef
            {
                nodeId = -1
            };
            nodeDB = new NodeDB();
            nodes = new Nodes();
            locals = new Dictionary<string, NodeRef>();
            globals = new Dictionary<string, NodeRef>();
            methods = new Dictionary<string, LogixMethod>(32);
            binaryOperationsNodes = new Dictionary<SyntaxKind, string>();
            currentOperationNodes = new List<OperationNodes>();

            nodeDB.AddInputDefinition("Input.BoolInput");
            nodeDB.AddInputDefinition("Input.ByteInput");
            nodeDB.AddInputDefinition("Input.SbyteInput");
            nodeDB.AddInputDefinition("Input.ShortInput");
            nodeDB.AddInputDefinition("Input.UshortInput");
            nodeDB.AddInputDefinition("Input.IntInput");
            nodeDB.AddInputDefinition("Input.UintInput");
            nodeDB.AddInputDefinition("Input.LongInput");
            nodeDB.AddInputDefinition("Input.UlongInput");
            nodeDB.AddInputDefinition("Input.FloatInput");
            nodeDB.AddInputDefinition("Input.DoubleInput");
            nodeDB.AddInputDefinition("Input.CharInput");
            nodeDB.AddInputDefinition("Input.StringInput");
            nodeDB.AddInputDefinition("Input.TimeNode");
            nodeDB.AddInputDefinition("Input.ColorInput");
            nodeDB.AddBinaryOperationDefinition("Operators.Add_Float");
            nodeDB.AddBinaryOperationDefinition("Operators.Add_Int");
            nodeDB.AddBinaryOperationDefinition("Operators.Mul_Float");
            nodeDB.AddBinaryOperationDefinition("Operators.Mul_Int");
            nodeDB.AddBinaryOperationDefinition("Operators.Div_Float");
            nodeDB.AddBinaryOperationDefinition("Operators.Div_Int");
            nodeDB.AddBinaryOperationDefinition("Operators.Sub_Float");
            nodeDB.AddBinaryOperationDefinition("Operators.Sub_Int");
            nodeDB.AddDefinition(
                "Data.ReadDynamicVariable", "Value",
                new string[] { "Source", "VariableName" },
                new string[] { "Value", "FoundValue" });
            nodeDB.AddDefinition(
                "Data.WriteOrCreateDynamicVariable", "",
                new string[] { "Target", "VariableName", "Value", "CreateDirectlyOnTarget", "CreateNonPersistent" },
                new string[] { });
            nodeDB.AddDefinition(
                "Color.HSV_ToColor", "*",
                new string[] { "H", "S", "V" },
                new string[] { "*" });


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

            typesList = new Dictionary<string, string>(16);
            typesList.Add("byte",   typeof(byte).FullName);
            typesList.Add("short",  typeof(short).FullName);
            typesList.Add("ushort", typeof(ushort).FullName);
            typesList.Add("char",   typeof(char).FullName);
            typesList.Add("int",    typeof(int).FullName);
            typesList.Add("uint",   typeof(uint).FullName);
            typesList.Add("long",   typeof(long).FullName);
            typesList.Add("ulong",  typeof(ulong).FullName);
            typesList.Add("float",  typeof(float).FullName);
            typesList.Add("double", typeof(double).FullName);
            typesList.Add("string", typeof(string).FullName);
            typesList.Add("object", typeof(object).FullName);
            typesList.Add("Color", "BaseX.color");

            

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

        private string GetDefaultOutput(int inputNodeID)
        {
            return nodeDB.DefaultOutputFor(nodes.GetNode(inputNodeID).GenericTypeName());
        }

        private bool CurrentImpulseValid()
        {
            return currentImpulseOutputName != null;
        }

        private void ImpulseNext(int outputNodeID, string nextImpulseName)
        {
            currentImpulseOutputNode = outputNodeID;
            currentImpulseOutputName = nextImpulseName;
        }

        private void ConnectImpulse(int inputNodeID, string inputName, string nextImpulseName)
        {
            if (CurrentImpulseValid())
            {
                Emit($"IMPULSE {inputNodeID} '{inputName}' FROM {currentImpulseOutputNode} '{currentImpulseOutputName}'");
                ImpulseNext(inputNodeID, nextImpulseName);
            }
        }

        private void Connect(int inputNodeID, string inputName, int outputNodeID)
        {
            /* FIXME : Don't always expect the default output to be '*'.
             * Get the information correctly
             */
            string outputName = GetDefaultOutput(outputNodeID);
            Emit($"INPUT {inputNodeID} '{inputName}' {outputNodeID} '{outputName}'");
        }

        private void Emit(string scriptLine)
        {
            script.Add(scriptLine);
            Console.WriteLine(scriptLine);
        }

        private void EmitPosition(int nodeID)
        {
            Emit($"POS {nodeID} {((int)nodePosition.X)} {(int)nodePosition.Y}");
        }

        public int AddNode(string typename, string name)
        {
            int newID = nodes.Count;
            string completeTypename = "FrooxEngine.LogiX." + typename;
            Node node = new Node
            {
                typename = completeTypename
            };
            nodes.Add(node);

            Emit($"NODE {newID} '{completeTypename}' \"{Base64Encode($"Node {newID} {name}")}\"");
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
                Emit($"SETCONST {nodeID} \"{Base64Encode(valueContent)}\"");
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

        public LogixMethod GetCurrentMethod()
        {
            return methods[currentMethodName];
        }

        private string LogixClassName(string cSharpTypeName)
        {
            return typesList[cSharpTypeName];
        }

        private int NodeDynamicVariableRead(string typeName, string varName, string nodeName)
        {
            int nodeID = AddNode($"Data.ReadDynamicVariable<{LogixClassName(typeName)}>", nodeName);
            int nodeNameID = DefineLiteral(typeof(string), varName);
            Connect(nodeID, "Tag", nodeNameID);
            return nodeID;
        }

        private int NodeDynamicVariableWrite(string typeName, string varName, string nodeName)
        {
            int nodeID = AddNode($"Data.CreateOrWriteDynamicVariable<{LogixClassName(typeName)}>", nodeName);
            int nodeNameID = DefineLiteral(typeof(string), varName);
            Connect(nodeID, "Tag", nodeNameID);
            return nodeID;
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var methodName = node.Identifier.ToString();
            methods.Add(methodName, new LogixMethod(methodName));
            currentMethodName = methodName;
            
            PositionNextForward();
            Console.WriteLine($"METHOD {node.Identifier} {node.ReturnType}");
            var parameters = node.ParameterList.Parameters;
            for (int i = 0; i < parameters.Count; i++)
            {
                Console.WriteLine($"PARAM {i+1} {parameters[i].Identifier}");

                var methodParam = parameters[i];
                string methodParamType = methodParam.Type.ToString();
                if (methodParamType == "float")
                {
                    /* FIXME : Try dynamic variables */
                    string paramName = methodParam.Identifier.ToString();
                    int nodeID = AddNode("Data.ReadDynamicVariable<System.Single>", $"{paramName}");
                    locals.Add(paramName, new NodeRef(nodeID));
                    GetCurrentMethod().AddParameter(paramName, methodParamType, nodeID);

                    int nameID = DefineLiteral(typeof(string), paramName);
                    Connect(nodeID, "VariableName", nameID);
                }
            }

            int methodRunImpulse = AddNode("ProgramFlow.DynamicImpulseReceiver", $"{methodName}");
            int tagName = DefineLiteral(typeof(string), methodName);
            Connect(methodRunImpulse, "Tag", tagName);

            string returnType = node.ReturnType.ToString();
            bool hasReturn = (returnType != "void");
            if (hasReturn)
            {
                currentReturnID = NodeDynamicVariableWrite(returnType, $"{methodName}/return", $"{methodName} Return");
            }
            else
            {
                currentReturnID = -1;
            }

            ImpulseNext(methodRunImpulse, "Impulse");

            base.VisitMethodDeclaration(node);
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            CollectionPush();
            base.VisitReturnStatement(node);
            OperationNodes nodes = CollectionPop();

            int returnedValueID = nodes[nodes.Count - 1];

            Connect(currentReturnID, "Value", returnedValueID);
            ConnectImpulse(currentReturnID, "WriteOrCreate", "OnWritten");
            
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
            NodeRef nodeRef = new NodeRef(nodeID);
            locals[varName] = nodeRef;
            Console.WriteLine($"VAR {node.Identifier} {nodeID}");


        }



        void CallingUserFunction(LogixMethod method, InvocationExpressionSyntax node)
        {
            /* Get arguments */
            CollectionPush();
            base.VisitInvocationExpression(node);
            OperationNodes nodes = CollectionPop();

            int nNodesParsed = Math.Min(nodes.Count, method.parameters.Count);

            /* Connect them to appropriate parameters calls */
            for (int i = 0; i < nNodesParsed; i++)
            {
                int nodeID = nodes[i];
                LogixMethodParameter methodParam = method.parameters[i];
                string nodeType = methodParam.type;
                if (!typesList.ContainsKey(nodeType))
                {
                    /* FIXME: If we hit this error, something is REALLY wrong
                     * within our code, since we prepared the parameter before.
                     */
                    Console.Error.WriteLine($"Can't handle parameter of type {nodeType}");
                    return;
                }
                string paramCallType = $"Data.WriteDynamicVariable<{typesList[nodeType]}>";

                /* FIXME: BUG: We still need to setup the name of
                 * the dynamic variable
                 */

                int paramSetNodeID = AddNode(paramCallType, $"Arg {i} {method.name}.{methodParam.name}");

                int paramNameID = DefineLiteral(typeof(string), $"{method.name}/{methodParam.name}");

                Connect(paramSetNodeID, "VariableName", paramNameID);
                Connect(paramSetNodeID, "Value", nodeID);

                int triggerNodeID = AddNode("ProgramFlow.DynamicImpulseTrigger", $"Calling {method.name}");

                int methodNameID = DefineLiteral(typeof(string), method.name);
                Connect(triggerNodeID, "Tag", methodNameID);

                ConnectImpulse(triggerNodeID, "Run", "OnTriggered");
            }
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            /* FIXME : This is just a quick hack for demonstration purposes.
             * Find a real design...
             */
            string nodeClass;
            string[] inputs;
            string functionName = node.Expression.ToString();

            if (methods.ContainsKey(functionName))
            {
                CallingUserFunction(methods[functionName], node);
                return;
            }
            
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
                Connect(functionNodeID, inputName, argumentNodeID);
                //Emit($"INPUT {functionNodeID} '{inputName}' {argumentNodeID} '{outputName}'");
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
                Connect(nodeID, "A", operands[0]);
                Connect(nodeID, "B", operands[1]);
                /*Emit($"INPUT {nodeID} 'A' {operands[0]} '*'");
                Emit($"INPUT {nodeID} 'B' {operands[1]} '*'");*/
                
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

        /*public override void VisitParameter(ParameterSyntax node)
        {

            base.VisitParameter(node);
        }*/

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
            Console.WriteLine("-----------------------");
            Console.WriteLine(script);

            string homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            File.WriteAllText(homePath + "/Documents/Neos VR/Sample.lgx", script);
            
        }
    }
}
