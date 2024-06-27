using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XNode;
using System;
using System.Linq;

namespace XNodeCore
{
    ///<summary>A node which can process ports before passing them. This is useful for caching large calculations and re-using them.</summary>
    public abstract class ProcessorNode : Node
    {
        public ProcessorGraph pGraph;

        public static List<InField> currentInFields = new List<InField>();
        public static List<OutField> currentOutFields = new List<OutField>();
        [NonSerialized] public List<InField> inFields = new List<InField>();
        [NonSerialized] public List<OutField> outFields = new List<OutField>();

        public ProcessorNode()
        {
            inFields = currentInFields;
            currentInFields = new List<InField>();

            outFields = currentOutFields;
            currentOutFields = new List<OutField>();
        }

        public new void OnEnable()
        {
            base.OnEnable();
            pGraph = ProcessorGraph.currentGraph;
            CreateDynamicFields();
        }

        public override object GetValue(NodePort port) => null;

        public void ConfigureInFields()
        {
            foreach (InField inField in inFields)
            {
                NodePort port = GetInputPort(inField.name);
                if (port != null && port.IsConnected) inField.connectedOutField = ((ProcessorNode)port.GetConnection(0).node).outFields.Find((o) => o.name == port.GetConnection(0).fieldName);
            }
        }

        public void GraphProcess()
        {
            foreach (InField inField in inFields)
            {
                inField.Configure();
            }

            Process();
        }

        public void ConfigureOutFields()
        {
            foreach (OutField outField in outFields)
            {
                NodePort port = GetOutputPort(outField.name);
                if (port != null && port.IsConnected) outField.connectedInField = ((ProcessorNode)port.GetConnection(0).node).inFields.Find((i) => i.name == port.GetConnection(0).fieldName);

                outField.ClearCache();
            }
        }

        public void CreateDynamicFields()
        {
            DynamicFields();

            foreach (InField field in currentInFields)
            {
                field.isDynamic = true;
                field.node = this;
                NodePort port = GetInputPort(field.name);
                if (port == null) port = AddDynamicInput(field.ValueType, fieldName: field.name);
            }
            foreach (OutField field in currentOutFields)
            {
                field.isDynamic = true;
                field.node = this;
                NodePort port = GetOutputPort(field.name);
                if (port == null) port = AddDynamicOutput(field.ValueType, fieldName: field.name);
            }

            inFields.AddRange(currentInFields);
            outFields.AddRange(currentOutFields);
            currentInFields.Clear();
            currentOutFields.Clear();
        }

        protected virtual void DynamicFields() { }

        #if UNITY_EDITOR
        public virtual Type DynamicType() => GetType();
        public virtual void DynamicCopy(ProcessorNode target) { }

        public virtual InField InputPortToField(string fieldName) => (InField)GetType().GetField(fieldName).GetValue(this);
        public InField InputPortToField(NodePort port) => InputPortToField(port.fieldName);

        public virtual OutField OutputPortToField(string fieldName) => (OutField)GetType().GetField(fieldName).GetValue(this);
        public OutField OutputPortToField(NodePort port) => OutputPortToField(port.fieldName);
        #endif

        ///<summary>Receive and process port information before passing it.</summary>
        public virtual void Process() { }
    }
}