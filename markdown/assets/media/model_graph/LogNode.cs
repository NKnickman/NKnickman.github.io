using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XNode;

namespace XNodeCore
{
    [CreateNodeMenu("Debug/Log")]
    public class LogNode : CoreNode
    {
        public InSingleField<object> input = new InSingleField<object>();

        public override void Process()
        {
            Debug.Log(input.Value);
        }
    }
}