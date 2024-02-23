﻿using Better.StateMachine.Runtime.Conditions;
using Better.StateMachine.Runtime.States;

namespace Better.StateMachine.Runtime.Transitions
{
    public class AnyToTransition<TState> : Transition<TState> where TState : BaseState
    {
        public AnyToTransition(TState to, ICondition condition)
            : base(to, condition)
        {
        }

        public override bool Validate(TState current)
        {
            return current != To && base.Validate(current);
        }
    }
}