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
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpLogix
{

    /*class NodeDefinition
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
        
    }*/

    struct NodeTypedConnector
    {
        public string name;
        public string logixType;

        public NodeTypedConnector(string name, string logixType)
        {
            this.name = name;
            this.logixType = logixType;
        }
    }

    struct NodeInfo
    {
        public string className;
        public int defaultOutputIndex;
        public NodeTypedConnector[] inputs;
        public NodeTypedConnector[] outputs;
        public string[] methods;
        public string[] impulses;

        public static NodeInfo Define(
            string cName,
            NodeTypedConnector[] nodeInputs,
            NodeTypedConnector[] nodeOutputs,
            int outputIndex)
        {
            return new NodeInfo
            {
                className = cName,
                defaultOutputIndex = outputIndex,
                inputs = nodeInputs,
                outputs = nodeOutputs,
                /* TODO Add methods and impulses ! */
                methods = new string[0],
                impulses = new string[0]
            };
        }

        public string DefaultOutputName()
        {
            return outputs[defaultOutputIndex].name;
        }

        public string OutputTypeOf(string outputName)
        {
            foreach (var nodeConnector in outputs)
            {
                if (nodeConnector.name == outputName)
                {
                    return nodeConnector.logixType;
                }
            }
            return "INVALID_OUTPUT_" + outputName;
        }
 
    }

    class NodeDB : Dictionary<string, NodeInfo>
    {
        public NodeInfo AddInputDefinition(string name, string nodeType)
        {
            NodeTypedConnector[] inputs = { };
            NodeTypedConnector[] outputs = { new NodeTypedConnector("*", nodeType) };
            return AddDefinition(name, inputs, outputs, 0);
        }

        public NodeInfo AddBinaryOperationDefinition(string name, string nodeType)
        {
            NodeTypedConnector[] inputs  = { 
                new NodeTypedConnector("A", nodeType),
                new NodeTypedConnector("B", nodeType)
            };
            NodeTypedConnector[] outputs = { 
                new NodeTypedConnector("*", nodeType)
            };
            return AddDefinition(name, inputs, outputs, 0);
        }

        public NodeInfo AddDefinition(
            string name,
            NodeTypedConnector[] inputs,
            NodeTypedConnector[] outputs,
            int defaultOutputIndex)
        {
            string completeName = "FrooxEngine.LogiX." + name;
            NodeInfo nodeInfo = NodeInfo.Define(completeName, inputs, outputs, defaultOutputIndex);
            this.Add(completeName, nodeInfo);
            return nodeInfo;
        }
        public NodeInfo GetNodeInformation(string nodeName)
        {
            return this[nodeName];
        }

        public string DefaultOutputFor(string nodeName)
        {
            return this[nodeName].DefaultOutputName();
        }

        public string OutputTypeOf(string nodeName, string outputName)
        {
            return this[nodeName].OutputTypeOf(outputName);
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

    struct LogixLocalVariable
    {
        public int lastNodeID;
        public int definitionLevel;
        public int lastUpdateLevel;
        public int checkPointFromID;
        public int checkPointID;

        static public readonly int invalidID = -1;

        public void SetLastNode(int nodeID, int level)
        {
            lastNodeID = nodeID;
            lastUpdateLevel = level;
            if (level == definitionLevel)
            {
                checkPointFromID = lastNodeID;
            }
        }

        public bool UpdatedFromLowerLevels()
        {
            return lastUpdateLevel != definitionLevel;
        }

        public void SetCheckPoint(int registerNodeID)
        {
            checkPointID = registerNodeID;
        }

        public void RemoveCheckPoint()
        {
            checkPointID = invalidID;
        }

        public bool CheckpointSet()
        {
            return checkPointID != invalidID;
        }
    };

    class LogixLocalVariables : Dictionary<string, LogixLocalVariable>
    {
        public int level = 0;
        public static LogixLocalVariable invalidVar = new LogixLocalVariable()
        {
            lastNodeID = LogixLocalVariable.invalidID,
            definitionLevel = LogixLocalVariable.invalidID,
            lastUpdateLevel = LogixLocalVariable.invalidID,
            checkPointFromID = LogixLocalVariable.invalidID,
            checkPointID = LogixLocalVariable.invalidID
        };

        public LogixLocalVariable AddVariable(string name, int lastNodeID)
        {
            LogixLocalVariable localVar = new LogixLocalVariable
            {
                lastNodeID = lastNodeID,
                definitionLevel = level,
                lastUpdateLevel = level,
                checkPointFromID = lastNodeID,
                checkPointID = LogixLocalVariable.invalidID
            };
            Add(name, localVar);
            return localVar;
        }

        public bool HasIdentifier(string name)
        {
            return this.ContainsKey(name);
        }

        public LogixLocalVariable SetVariable(string name, int lastNodeID, int level)
        {
            LogixLocalVariable localVar = invalidVar;
            if (HasIdentifier(name))
            {
                this[name].SetLastNode(lastNodeID, level);
            }
            return localVar;
        }
    }

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
        public string returnType;
        public int slotID;

        public bool ReturnValue()
        {
            return returnType != "void";
        }

        public void SetReturnType(string typeName)
        {
            returnType = typeName;
        }

        static readonly LogixMethodParameter invalidParam = new LogixMethodParameter("", "", -1);

        public LogixMethod(string methodName, int newSlotID)
        {
            name = methodName;
            slotID = newSlotID;
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

    struct LogixSlot
    {
        public string name;

        public LogixSlot(string slotName)
        {
            name = slotName;
        }
    }

    class Slots : List<LogixSlot>
    {
        public int AddSlot(string name)
        {
            int slotID = Count;
            Add(new LogixSlot(name));
            return slotID;
        }

        public LogixSlot GetSlot(int slotID)
        {
            return this[slotID];
        }
    }

    class SharpenedSyntaxWalker : CSharpSyntaxWalker
    {

        readonly NodeDB nodeDB;

        readonly Nodes nodes;
        readonly Slots slots;
        int currentSlotID = -1;
        readonly List<string> script;
        System.Numerics.Vector2 nodePosition;

        List<OperationNodes> currentOperationNodes;
        readonly List<LogixLocalVariables> locals;
        int currentBlockLevel = 0; // Maybe unused
        bool localsAlreadyPrepared = false;
        bool generateCheckpoints = false;
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

        readonly Dictionary<TypeCode, string> literalLogixNodes;
        readonly Dictionary<SyntaxKind, string> binaryOperationsNodes;
        /* FIXME : Find a better name */
        readonly Dictionary<string, string> typesList;


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
            slots = new Slots();
            currentSlotID = slots.AddSlot("ProgramSlot");
            locals = new List<LogixLocalVariables>(4);
            globals = new Dictionary<string, NodeRef>();
            methods = new Dictionary<string, LogixMethod>(32);
            binaryOperationsNodes = new Dictionary<SyntaxKind, string>();
            currentOperationNodes = new List<OperationNodes>();

            nodeDB.AddInputDefinition("Input.BoolInput",   "System.Boolean");
            nodeDB.AddInputDefinition("Input.ByteInput",   "System.Byte");
            nodeDB.AddInputDefinition("Input.SbyteInput",  "System.SByte");
            nodeDB.AddInputDefinition("Input.ShortInput",  "System.Int16");
            nodeDB.AddInputDefinition("Input.UshortInput", "System.UInt16");
            nodeDB.AddInputDefinition("Input.IntInput",    "System.Int32");
            nodeDB.AddInputDefinition("Input.UintInput",   "System.UInt32");
            nodeDB.AddInputDefinition("Input.LongInput",   "System.Int64");
            nodeDB.AddInputDefinition("Input.UlongInput",  "System.UInt64");
            nodeDB.AddInputDefinition("Input.FloatInput",  "System.Single");
            nodeDB.AddInputDefinition("Input.DoubleInput", "System.Double");
            nodeDB.AddInputDefinition("Input.CharInput",   "System.Char");
            nodeDB.AddInputDefinition("Input.StringInput", "System.String");
            nodeDB.AddInputDefinition("Input.TimeNode",    "System.DateTime");
            nodeDB.AddInputDefinition("Input.ColorInput",  "BaseX.color");
            nodeDB.AddBinaryOperationDefinition("Operators.Add_Float",             "System.Single");
            nodeDB.AddBinaryOperationDefinition("Operators.Add_Int",               "System.Int32");
            nodeDB.AddBinaryOperationDefinition("Operators.Mul_Float",             "System.Single");
            nodeDB.AddBinaryOperationDefinition("Operators.Mul_Int",               "System.Int32");
            nodeDB.AddBinaryOperationDefinition("Operators.Div_Float",             "System.Single");
            nodeDB.AddBinaryOperationDefinition("Operators.Div_Int",               "System.Int32");
            nodeDB.AddBinaryOperationDefinition("Operators.Sub_Float",             "System.Single");
            nodeDB.AddBinaryOperationDefinition("Operators.Sub_Int",               "System.Int32");
            nodeDB.AddBinaryOperationDefinition("Operators.GreaterThan_Float",     "System.Single");
            nodeDB.AddBinaryOperationDefinition("Operators.GreaterOrEqual_Float",  "System.Single");
            nodeDB.AddBinaryOperationDefinition("Operators.Equals_Float",          "System.Single");
            nodeDB.AddBinaryOperationDefinition("Operators.LessThan_Float",        "System.Single");

            nodeDB.AddDefinition(
                "Data.ReadDynamicVariable",
                new NodeTypedConnector[] { 
                    new NodeTypedConnector("Source", "`1"),
                    new NodeTypedConnector("VariableName", "System.String")
                },
                new NodeTypedConnector[] { "Value", "FoundValue" },
                0);
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

            /* FIXME : Autodetect the type. Try automatic coercion if possible. */
            binaryOperationsNodes.Add(SyntaxKind.AddExpression,        "Add_Int");
            binaryOperationsNodes.Add(SyntaxKind.SubtractExpression,   "Sub_Int");
            binaryOperationsNodes.Add(SyntaxKind.MultiplyExpression,   "Mul_Float");
            binaryOperationsNodes.Add(SyntaxKind.DivideExpression,     "Div_Int");
            binaryOperationsNodes.Add(SyntaxKind.BitwiseAndExpression, "AND_Bool");

            binaryOperationsNodes.Add(SyntaxKind.GreaterThanExpression, "GreaterThan_Float");
            binaryOperationsNodes.Add(SyntaxKind.GreaterThanOrEqualExpression, "GreaterOrEqual_Float");
            binaryOperationsNodes.Add(SyntaxKind.EqualsExpression, "Equals_Float");
            binaryOperationsNodes.Add(SyntaxKind.LessThanExpression, "LessThan_Float");
            /* And you might wonder where the LesserThanExpression expression went ?
             * WELL, it's not directly supported in LogiX.
             * So we'll have to hack around with "NOT GREATER_THAN"
             */

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

        public LogixLocalVariables LocalsGet()
        {
            return locals[locals.Count - 1];
        }

        public bool LocalVariableValid(LogixLocalVariable localVar)
        {
            return localVar.definitionLevel >= 0;
        }

        public LogixLocalVariables LocalsContainingIdentifier(string identifier)
        {
            for (int level = currentBlockLevel; level >= 0; level++)
            {
                LogixLocalVariables levelVariables = locals[level];
                if (levelVariables.HasIdentifier(identifier))
                {
                    return levelVariables;
                }
            }
            return null;
        }

        public LogixLocalVariable LocalsGetVariable(string name)
        {
            LogixLocalVariables vars = LocalsContainingIdentifier(name);
            LogixLocalVariable var = LogixLocalVariables.invalidVar;
            if (vars != null)
            {
                var = vars[name];
            }
            return var;
        }

        public LogixLocalVariables LocalsPush()
        {
            currentBlockLevel++;
            LogixLocalVariables localVars = new LogixLocalVariables
            {
                level = locals.Count
            };
            locals.Add(localVars);
            return localVars;
        }

        public LogixLocalVariables LocalsPop()
        {
            currentBlockLevel--;
            LogixLocalVariables currentLevel = LocalsGet();
            locals.RemoveAt(locals.Count - 1);
            return currentLevel;
        }

        public LogixLocalVariable LocalAdd(string name, int nodeID)
        {
            return LocalsGet().AddVariable(name, nodeID);
        }

        public LogixLocalVariable LocalsSetVar(string name, int nodeID)
        {
            LogixLocalVariable localVar = LogixLocalVariables.invalidVar;
            LogixLocalVariables varsWithName = LocalsContainingIdentifier(name);
            
            if (varsWithName != null)
            {
                localVar = varsWithName.SetVariable(name, nodeID, currentBlockLevel);
            }
            return localVar;
        }

        public void PositionNextBottom()
        {
            nodePosition.Y += 75;
        }

        public void PositionNextForward(int forward = 150)
        {
            nodePosition.X += forward;
            nodePosition.Y  = 0;
        }

        public void PositionSet(Vector2 position)
        {
            nodePosition = position;
        }

        public Vector2 PositionGet()
        {
            return nodePosition;
        }

        private string NodeOutputType(int nodeID, string outputName)
        {
            string nodeType = nodes.GetNode(nodeID).GenericTypeName();
            
            nodeDB[nodeType]
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
                Emit($"IMPULSE {inputNodeID} '{inputName}' {currentImpulseOutputNode} '{currentImpulseOutputName}'");
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

        private int RegisterCreateFor(int nodeID, string outputName)
        {
            
            
            
        }

        private int LocalCheckpointCreate(LogixLocalVariable localVariable)
        {
            int checkpointFromID = localVariable.checkPointFromID;
            /* FIXME What if we don't want to use the Default output ? */
            int registerID = RegisterCreateFor(checkpointFromID, GetDefaultOutput(checkpointFromID));
        }

        private void LocalCheckpointConnectTo(LogixLocalVariable localVariable)
        {
            int checkpointID = localVariable.checkPointID;
            if (localVariable.CheckpointSet() == false)
            {
                checkpointID = LocalCheckpointCreate(localVariable);
            }
            
        }

        private void LocalsWriteBackUpdatedBefore(int level)
        {
            int upto = Math.Max(0, Math.Min(level, locals.Count - 1));
            for (int i = 0; i < upto; i++)
            {
                foreach (var localVariable in locals[i].Values)
                {
                    if (localVariable.UpdatedFromLowerLevels())
                    {
                        LocalCheckpointConnectTo(localVariable);
                    }
                }
            }
        }

        public override void VisitBlock(BlockSyntax node)
        {
            
            /* FIXME : Replace this by "Parameters To Copy"
             * Or integrate the write back into LocalsPush/Pop ?
             */
            if (!localsAlreadyPrepared)
            {
                LocalsWriteBackUpdatedInLowerLevels();
                LogixLocalVariables blockLocalVariables = LocalsPush();
            }
            /* FIXME : Check if we need this variable */
            base.VisitBlock(node);
            LocalsWriteBackUpdatedInLowerLevels();
            LocalsPop();
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            string identifierName = node.Identifier.ToString();
            /* FIXME : Search in every variable category */
            var localVar = LocalsGetVariable(identifierName);
            if (LocalVariableValid(localVar))
            {
                AddToCurrentOperation(localVar.lastNodeID);
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

        private void ConnectWithSlot(int nodeID, string nodeInputName, int slotID)
        {
            Emit($"SETNODESLOT {nodeID} '{nodeInputName}' S{slotID}");
        }

        /* FIXME Factorize with Write */
        private int NodeDynamicVariableRead(string typeName, string varName, string nodeName, int slotID)
        {
            int nodeID = AddNode($"Data.ReadDynamicVariable<{LogixClassName(typeName)}>", nodeName);
            int nodeNameID = DefineLiteral(typeof(string), varName);
            Connect(nodeID, "VariableName", nodeNameID);

            string nodeSlotInputName = "Source";
            if (slotID > -1)
            {
                ConnectWithSlot(nodeID, nodeSlotInputName, slotID);
            }
            return nodeID;
        }

        private int NodeDynamicVariableWrite(string typeName, string varName, string nodeName, int slotID)
        {
            int nodeID = AddNode($"Data.WriteDynamicVariable<{LogixClassName(typeName)}>", nodeName);
            int nodeNameID = DefineLiteral(typeof(string), varName);
            Connect(nodeID, "VariableName", nodeNameID);

            string nodeSlotInputName = "Target";
            if (slotID > -1)
            {
                ConnectWithSlot(nodeID, nodeSlotInputName, slotID);
            }
            return nodeID;
        }

        void VariableDefine(string varName, string varType, int slotID)
        {
            Emit($"VAR S{slotID} \"{Base64Encode(varName)}\" '{LogixClassName(varType)}' ");
        }

        int SlotAdd(string slotName)
        {
            int newSlotID = slots.AddSlot(slotName);
            Emit($"SLOT S{newSlotID} \"{Base64Encode(slotName)}\"");
            return newSlotID;
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var methodName = node.Identifier.ToString();
            int methodSlot = SlotAdd(methodName);
            currentSlotID = methodSlot;

            LogixMethod logixMethod = new LogixMethod(methodName, methodSlot);
            methods.Add(methodName, logixMethod);
            currentMethodName = methodName;

            /* FIXME Have another way to deal with the Layout */
            PositionNextForward(700);
            Console.WriteLine($"METHOD {node.Identifier} {node.ReturnType}");
            var parameters = node.ParameterList.Parameters;
            for (int i = 0; i < parameters.Count; i++)
            {
                Console.WriteLine($"PARAM {i+1} {parameters[i].Identifier}");

                var methodParam = parameters[i];
                string methodParamType = methodParam.Type.ToString();
                string paramName = methodParam.Identifier.ToString();
                VariableDefine(paramName, methodParamType, methodSlot);
                int nodeID = NodeDynamicVariableRead(
                    methodParamType, paramName,
                    "Param : " + paramName, methodSlot);
                /* FIXME
                 * We need to add locals before visiting the block here.
                 * But we also need to take care of random blocks defined
                 * to fragment codes in a function...
                 */
                locals.Add(paramName, new NodeRef(nodeID));
                logixMethod.AddParameter(paramName, methodParamType, nodeID);
            }

            /* FIXME Have another way to deal with the Layout */
            if (parameters.Count != 0)
            {
                PositionNextForward(300);
            }
            int methodRunImpulse = AddNode("ProgramFlow.DynamicImpulseReceiver", $"{methodName}");
            int tagName = DefineLiteral(typeof(string), methodName);
            Connect(methodRunImpulse, "Tag", tagName);


            string returnType = node.ReturnType.ToString();
            logixMethod.SetReturnType(returnType);
            bool hasReturn = (returnType != "void");
            if (hasReturn)
            {
                VariableDefine("return", returnType, methodSlot);
                currentReturnID = NodeDynamicVariableWrite(returnType, "return", $"{methodName} Return", methodSlot);
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
            ConnectImpulse(currentReturnID, "Write", "OnSuccess");
            
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

                Console.WriteLine("Calling user function");

                int paramSetNodeID = NodeDynamicVariableWrite(methodParam.type, $"{methodParam.name}", $"SetArg {methodParam.name}", method.slotID);
                Connect(paramSetNodeID, "Value", nodeID);

                ConnectImpulse(paramSetNodeID, "Write", "OnSuccess");
                int triggerNodeID = AddNode("ProgramFlow.DynamicImpulseTrigger", $"Calling {method.name}");
                ConnectWithSlot(triggerNodeID, "TargetHierarchy", method.slotID);
                int methodNameID = DefineLiteral(typeof(string), method.name);
                Connect(triggerNodeID, "Tag", methodNameID);

                ConnectImpulse(triggerNodeID, "Run", "OnTriggered");

                if (method.ReturnValue())
                {
                    NodeDynamicVariableRead(method.returnType, $"return", $"Read {method.name} return", method.slotID);
                }
                
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

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            /* TODO
             * - Add a Sequence
             * - Flow from the Sequence after the If statement
             * This requires support for Inputs lists, so this will
             * require some tests on the plugin side...
             */

            CollectionPush();
            base.Visit(node.Condition);
            OperationNodes nodes = CollectionPop();

            if (nodes.Count == 0)
            {
                Console.Error.WriteLine("Could not parse IF condition...");
                return;
            }


            int sequenceNodeID = AddNode("ProgramFlow.SequenceImpulse", "Branching");
            /* I purposedly do not encode the end bracket in the
             * sequence, since this just add extra parsing
             * for no reasons.
             * This might be replaced by a comma, to avoid triggering
             * people OCD (and make parsing easier)
             */
            ConnectImpulse(sequenceNodeID, "Trigger", "Sequence[0");

            /* FIXME: Wishful thinking. Nothing guarantees that the last node
             * is returning a boolean. */
            int boolNodeID = nodes[nodes.Count - 1];
            int ifNodeID = AddNode("ProgramFlow.IfNode", "IF Statement");

            Connect(ifNodeID, "Condition", boolNodeID);
            ConnectImpulse(ifNodeID, "Run", "True");

            base.Visit(node.Statement);

            if (node.Else != null)
            {
                ImpulseNext(currentImpulseOutputNode, "False");
                base.Visit(node.Else);
            }
            Console.WriteLine($"IF CONDITION : {node.Condition} THEN : {node.Statement} ELSE : {node.Else}");

            ImpulseNext(sequenceNodeID, "Sequence[1");
            //base.VisitIfStatement(node);
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
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

            SyntaxTree tree = CSharpSyntaxTree.ParseText(@"

                Color RandomColor(float currentTime)
                {
                    return Color.FromHSV(currentTime, 1.0f, 1.0f);
                }
                public void WonderfulMethod(float speed)
                {
                    float time = Time.CurrentTime() * speed;
                    if (speed == 0.0f)
                    {
                        time = 1.0f;
                    }
                    
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
