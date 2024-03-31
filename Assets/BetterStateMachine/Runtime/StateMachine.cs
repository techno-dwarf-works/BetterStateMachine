﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Better.Extensions.Runtime;
using Better.StateMachine.Runtime.Modules;
using Better.StateMachine.Runtime.Sequences;
using Better.StateMachine.Runtime.States;
using UnityEngine;

namespace Better.StateMachine.Runtime
{
    [Serializable]
    public class StateMachine<TState, TTransitionSequence> : IStateMachine<TState> where TState : BaseState
        where TTransitionSequence : ISequence<TState>, new()
    {
        public event Action<TState> StateChanged;

        private CancellationTokenSource _runningTokenSource;
        private CancellationTokenSource _transitionTokenSource;
        private readonly TTransitionSequence _transitionSequence;
        private TaskCompletionSource<bool> _stateChangeCompletionSource;
        private Dictionary<Type, Module<TState>> _typeModuleMap;

        public bool IsRunning { get; protected set; }
        public bool InTransition => _stateChangeCompletionSource != null;
        public Task TransitionTask => InTransition ? _stateChangeCompletionSource.Task : Task.CompletedTask;
        public TState CurrentState { get; protected set; }

        public StateMachine(TTransitionSequence transitionSequence)
        {
            if (transitionSequence == null)
            {
                throw new ArgumentNullException(nameof(transitionSequence));
            }

            _transitionSequence = transitionSequence;
            _typeModuleMap = new();
        }

        public StateMachine() : this(new())
        {
        }

        public virtual void Run()
        {
            if (!ValidateRunning(false))
            {
                return;
            }

            foreach (var module in _typeModuleMap.Values)
            {
                if (!module.AllowRunMachine())
                {
                    var message = $"{module} not allow machine run";
                    Debug.LogWarning(message);

                    return;
                }
            }

            IsRunning = true;
            _runningTokenSource = new CancellationTokenSource();

            foreach (var module in _typeModuleMap.Values)
            {
                module.OnMachineRunned();
            }
        }

        public virtual void Stop()
        {
            if (!ValidateRunning(true))
            {
                return;
            }

            foreach (var module in _typeModuleMap.Values)
            {
                if (!module.AllowStopMachine())
                {
                    var message = $"{module} not allow machine stop";
                    Debug.LogWarning(message);

                    return;
                }
            }

            IsRunning = false;
            _runningTokenSource?.Cancel();

            foreach (var module in _typeModuleMap.Values)
            {
                module.OnMachineStopped();
            }
        }

        #region States

        public async Task ChangeStateAsync(TState newState, CancellationToken cancellationToken)
        {
            if (!ValidateRunning(true))
            {
                return;
            }

            if (newState == null)
            {
                DebugUtility.LogException<ArgumentNullException>(nameof(newState));
                return;
            }

            foreach (var module in _typeModuleMap.Values)
            {
                if (!module.AllowChangeState(newState))
                {
                    var message = $"{module} not allow change state to {newState}";
                    Debug.LogWarning(message);

                    return;
                }
            }

            _transitionTokenSource?.Cancel();
            await TransitionTask;

            _transitionTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_runningTokenSource.Token, cancellationToken);
            _stateChangeCompletionSource = new TaskCompletionSource<bool>();

            CurrentState = await _transitionSequence.ChangingStateAsync(CurrentState, newState, _transitionTokenSource.Token);
            OnStateChanged(CurrentState);

            _stateChangeCompletionSource.TrySetResult(true);
            _stateChangeCompletionSource = null;
        }

        public void ChangeState(TState newState)
        {
            ChangeStateAsync(newState, CancellationToken.None).Forget();
        }

        protected virtual void OnStateChanged(TState state)
        {
            foreach (var module in _typeModuleMap.Values)
            {
                module.OnStateChanged(state);
            }

            StateChanged?.Invoke(state);
        }

        public bool InState<T>() where T : TState
        {
            return CurrentState is T;
        }

        #endregion

        #region Modules

        public void AddModule(Module<TState> module)
        {
            if (module == null)
            {
                DebugUtility.LogException<ArgumentNullException>(nameof(module));
                return;
            }

            if (!ValidateRunning(false))
            {
                return;
            }

            var type = module.GetType();
            if (HasModule(type))
            {
                var message = $"Module of {nameof(type)}({type}) already added";
                Debug.LogWarning(message);
                return;
            }

            _typeModuleMap.Add(type, module);
            module.Link(this);
        }

        public bool HasModule(Type type)
        {
            return _typeModuleMap.ContainsKey(type);
        }

        public bool HasModule<TModule>()
            where TModule : Module<TState>
        {
            var type = typeof(TModule);
            return HasModule(type);
        }

        public bool TryGetModule(Type type, out Module<TState> module)
        {
            return _typeModuleMap.TryGetValue(type, out module);
        }

        public bool TryGetModule<TModule>(out TModule module)
            where TModule : Module<TState>
        {
            var type = typeof(TModule);
            if (TryGetModule(type, out var mappedModule))
            {
                module = (TModule)mappedModule;
                return true;
            }

            module = null;
            return false;
        }

        public Module<TState> GetModule(Type type)
        {
            if (TryGetModule(type, out var module))
            {
                return module;
            }

            var message = $"Module of {nameof(type)}({type}) not found";
            DebugUtility.LogException<InvalidOperationException>(message);
            return null;
        }

        public TModule GetModule<TModule>()
            where TModule : Module<TState>
        {
            if (TryGetModule<TModule>(out var module))
            {
                return module;
            }

            var type = typeof(TModule);
            var message = $"Module of {nameof(type)}({type}) not found";
            DebugUtility.LogException<InvalidOperationException>(message);
            return null;
        }

        public bool RemoveModule(Type type)
        {
            if (_typeModuleMap.TryGetValue(type, out var module)
                && _typeModuleMap.Remove(type))
            {
                module.Unlink();
                return true;
            }

            return false;
        }

        public bool RemoveModule<TModule>()
            where TModule : Module<TState>
        {
            var type = typeof(TModule);
            return RemoveModule(type);
        }

        #endregion

        private bool ValidateRunning(bool targetState, bool logException = true)
        {
            var isValid = IsRunning == targetState;
            if (!isValid && logException)
            {
                var reason = targetState ? "not running" : "is running";
                var message = "Is not valid, " + reason;
                DebugUtility.LogException<InvalidOperationException>(message);
            }

            return isValid;
        }
    }

    [Serializable]
    public class StateMachine<TState> : StateMachine<TState, DefaultSequence<TState>>
        where TState : BaseState
    {
        public StateMachine(DefaultSequence<TState> transitionSequence) : base(transitionSequence)
        {
        }

        public StateMachine() : this(new())
        {
        }
    }
}