using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XNode;
using System;
using System.Linq;
using Unity.Collections;

#if UNITY_EDITOR
using XNodeEditor;
#endif

namespace XNodeCore
{
    ///<summary>A graph whose nodes can process ports before passing them. This is useful for caching large calculations and re-using them.</summary>
    [CreateAssetMenu]
    public class ProcessorGraph : NodeGraph
    {
        public static ProcessorGraph currentGraph;

        public StringList<Parameter> parameters = new StringList<Parameter>();
        public ProcessorGraph inheritParameters;

        public virtual Type[] IncludeNodeTypes { get; } = new Type[] { typeof(ProcessorNode) };
        public virtual Type[] ExcludeNodeTypes { get; } = new Type[0];

        private HashSet<Tuple<int, int>> processedFields = new HashSet<Tuple<int, int>>();
        private HashSet<ProcessorNode> processedNodes = new HashSet<ProcessorNode>();
        public void Process()
        {
            Field.converter.cachedConversions.Clear();
            processedFields.Clear();
            processedNodes.Clear();
            foreach (ProcessorNode capNode in nodes.FindAll((n) =>
            {
                ProcessorNode pN = n as ProcessorNode;
                return pN != null && !pN.outFields.Any((oF) => oF.Connected == true);
            }))
            {
                ProcessRecursive(capNode);
            }
        }

        public ProcessorGraph() => currentGraph = this;

        private void ProcessBranch(Field a, Field b)
        {
            Tuple<int, int> pair = new Tuple<int, int>(a.GetHashCode(), b.GetHashCode());

            if (!processedFields.Contains(pair))
            {
                processedFields.Add(pair);
                ProcessRecursive(b.node);
            }
        }

        private void ProcessRecursive(ProcessorNode node)
        {
            bool processed = processedNodes.Add(node);
            if (processed) node.ConfigureInFields();

            // Process All Needed Inputs
            foreach (InField inField in node.inFields)
            {
                if (inField.Connected)
                {
                    ProcessBranch(inField, inField.connectedOutField);
                }
            }

            if (processed)
            {
                node.GraphProcess();
                node.ConfigureOutFields();
            }
        }

        private static readonly Type[] validTypes = new Type[]
        {
            typeof(InSingleField<>),
            typeof(InMultiField<>),
            typeof(OutSingleField<>),
            typeof(OutMultiField<>)
        };

        private static readonly Type[] inputTypes = new Type[]
        {
            typeof(InSingleField<>),
            typeof(InMultiField<>)
        };

        private static readonly Type[] outputTypes = new Type[]
        {
            typeof(OutSingleField<>),
            typeof(OutMultiField<>)
        };

        private static readonly Type[] multiTypes = new Type[]
        {
            typeof(InMultiField<>),
            typeof(OutMultiField<>)
        };

        public static bool IsValid(Type type) => validTypes.Contains(type.GetGenericTypeDefinition());
        public static bool IsInput(Type type) => inputTypes.Contains(type.GetGenericTypeDefinition());
        public static bool IsOutput(Type type) => outputTypes.Contains(type.GetGenericTypeDefinition());
        public static bool IsMulti(Type type) => multiTypes.Contains(type.GetGenericTypeDefinition());
        public static bool IsSingle(Type type) => !IsMulti(type);

        #if UNITY_EDITOR

        public static void SetCircleTexture(NodePort port)
        {
            NodeEditorWindow.current.graphEditor.GetPortStyle(port).normal.background = NodeEditorResources.dotOuter;
            NodeEditorWindow.current.graphEditor.GetPortStyle(port).active.background = NodeEditorResources.dot;
        }

        public static void SetTriangleTexture(NodePort port)
        {
            NodeEditorWindow.current.graphEditor.GetPortStyle(port).normal.background = Resources.Load<Texture2D>("xnodecore_tri_outer");
            NodeEditorWindow.current.graphEditor.GetPortStyle(port).active.background = Resources.Load<Texture2D>("xnodecore_tri");
        }

        public static void SetHollowTriangleTexture(NodePort port)
        {
            NodeEditorWindow.current.graphEditor.GetPortStyle(port).normal.background = Resources.Load<Texture2D>("xnodecore_tri_outer");
            NodeEditorWindow.current.graphEditor.GetPortStyle(port).active.background = Resources.Load<Texture2D>("xnodecore_tri_hollow");
        }

        public static void SetPortTexture(NodePort port)
        {
            //Debug.Log(port.ValueType);
            if (port.ValueType.IsGenericType && IsMulti(port.ValueType))
            {
                if (port.IsConnected && IsSingle(port.GetConnection(0).ValueType)) SetHollowTriangleTexture(port);
                else if (port.IsConnected || port.IsOutput) SetTriangleTexture(port);
                else SetHollowTriangleTexture(port);
                return;
            }
            else SetCircleTexture(port);
        }

        #endif
    }
}