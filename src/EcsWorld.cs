// ----------------------------------------------------------------------------
// The MIT License
// Simple Entity Component System framework https://github.com/Leopotam/ecs
// Copyright (c) 2017-2018 Leopotam <leopotam@gmail.com>
// ----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Leopotam.Ecs.Internals;

#if ENABLE_IL2CPP
// Unity IL2CPP performance optimization attribute.
namespace Unity.IL2CPP.CompilerServices {
    enum Option {
        NullChecks = 1,
        ArrayBoundsChecks = 2
    }

    [AttributeUsage (AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
    class Il2CppSetOptionAttribute : Attribute {
        public Option Option { get; private set; }
        public object Value { get; private set; }

        public Il2CppSetOptionAttribute (Option option, object value) { Option = option; Value = value; }
    }
}
#endif

namespace Leopotam.Ecs {
#if DEBUG
    /// <summary>
    /// Debug interface for world events processing.
    /// </summary>
    public interface IEcsWorldDebugListener {
        void OnEntityCreated (int entity);
        void OnEntityRemoved (int entity);
        void OnComponentAdded (int entity, object component);
        void OnComponentRemoved (int entity, object component);
        void OnWorldDestroyed (EcsWorld world);
    }
#endif
#if LEOECS_ENABLE_WORLD_EVENTS
    /// <summary>
    /// Interface for world events processing.
    /// </summary>
    public interface IEcsWorldEventListener {
        void OnEntityCreated (int entity);
        void OnEntityRemoved (int entity);
        void OnComponentAdded (int entity, object component);
        void OnComponentRemoved (int entity, object component);
        void OnWorldDestroyed (EcsWorld world);
    }
#endif

    /// <summary>
    /// Basic ecs world implementation.
    /// </summary>
#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public class EcsWorld : IDisposable {
        /// <summary>
        /// Last created instance of EcsWorld.
        /// Can be force reassigned manually when multiple worlds in use.
        /// </summary>
        public static EcsWorld Active = null;

        /// <summary>
        /// List of all entities (their components).
        /// </summary>
        EcsEntity[] _entities = new EcsEntity[1024];

        int _entitiesCount;

        /// <summary>
        /// List of removed entities - they can be reused later.
        /// </summary>
        int[] _reservedEntities = new int[256];

        int _reservedEntitiesCount;

        /// <summary>
        /// List of add / remove operations for components on entities.
        /// </summary>
        DelayedUpdate[] _delayedUpdates = new DelayedUpdate[1024];

        int _delayedUpdatesCount;

        /// <summary>
        /// List of requested filters.
        /// </summary>
        EcsFilter[] _filters = new EcsFilter[64];

        int _filtersCount;

        /// <summary>
        /// List of requested one-frame component filters.
        /// </summary>
        readonly Dictionary<int, EcsFilter> _oneFrameFilters = new Dictionary<int, EcsFilter> (32);

        /// <summary>
        /// Temporary buffer for filter updates.
        /// </summary>
        readonly EcsComponentMask _delayedOpMask = new EcsComponentMask ();

#if DEBUG
        bool _isDisposed;
#endif

        /// <summary>
        /// Creates new instance of ecs world container.
        /// </summary>
        /// <param name="useAsActive">Should this instance be set as Active or not.</param>
        public EcsWorld (bool useAsActive = true) {
            if (useAsActive) {
                Active = this;
            }
        }

        /// <summary>
        /// Destroys all registered external data, full cleanup for internal data.
        /// </summary>
        public void Dispose () {
#if DEBUG
            if (_isDisposed) { throw new Exception ("World already disposed"); }
            _isDisposed = true;
            for (var i = _debugListeners.Count - 1; i >= 0; i--) {
                _debugListeners[i].OnWorldDestroyed (this);
            }
#endif
#if LEOECS_ENABLE_WORLD_EVENTS
            for (var i = _eventListeners.Count - 1; i >= 0; i--) {
                _eventListeners[i].OnWorldDestroyed (this);
            }
#endif
            if (this == Active) {
                Active = null;
            }
            for (var i = 0; i < _entitiesCount; i++) {
                // already reserved entities cant contains components.
                if (_entities[i].ComponentsCount > 0) {
                    var entity = _entities[i];
                    for (var ii = 0; ii < entity.ComponentsCount; ii++) {
                        entity.Components[ii].Pool.RecycleById (entity.Components[ii].ItemId);
                    }
                }
            }
            // any next usage of this EcsWorld instance will throw exception.
            _entities = null;
            _entitiesCount = 0;
            _filters = null;
            _filtersCount = 0;
            _oneFrameFilters.Clear ();
            _reservedEntities = null;
            _reservedEntitiesCount = 0;
            _delayedUpdates = null;
            _delayedUpdatesCount = 0;
        }

#if DEBUG
        /// <summary>
        /// List of all debug listeners.
        /// </summary>
        readonly System.Collections.Generic.List<IEcsWorldDebugListener> _debugListeners = new System.Collections.Generic.List<IEcsWorldDebugListener> (4);

        /// <summary>
        /// Adds external event listener.
        /// </summary>
        /// <param name="listener">Event listener.</param>
        public void AddDebugListener (IEcsWorldDebugListener listener) {
            if (listener == null) { throw new Exception ("Listener is null"); }
            if (_debugListeners.Contains (listener)) { throw new Exception ("Listener already exists"); }
            _debugListeners.Add (listener);
        }

        /// <summary>
        /// Removes external event listener.
        /// </summary>
        /// <param name="listener">Event listener.</param>
        public void RemoveDebugListener (IEcsWorldDebugListener listener) {
            if (listener == null) { throw new Exception ("Listener is null"); }
            _debugListeners.Remove (listener);
        }
#endif

#if LEOECS_ENABLE_WORLD_EVENTS
        /// <summary>
        /// List of all event listeners.
        /// </summary>
        readonly System.Collections.Generic.List<IEcsWorldEventListener> _eventListeners = new System.Collections.Generic.List<IEcsWorldEventListener> (4);

        /// <summary>
        /// Adds external event listener.
        /// </summary>
        /// <param name="listener">Event listener.</param>
        public void AddEventListener (IEcsWorldEventListener listener) {
#if DEBUG
            if (listener == null) { throw new Exception ("Listener is null"); }
            if (_eventListeners.Contains (listener)) { throw new Exception ("Listener already exists"); }
#endif
            _eventListeners.Add (listener);
        }

        /// <summary>
        /// Removes external event listener.
        /// </summary>
        /// <param name="listener">Event listener.</param>
        public void RemoveEventListener (IEcsWorldEventListener listener) {
#if DEBUG
            if (listener == null) { throw new Exception ("Listener is null"); }
#endif
            _eventListeners.Remove (listener);
        }
#endif

        /// <summary>
        /// Creates empty entity.
        /// Important: If no components will be added - this entity will be automatically collected as garbage.
        /// </summary>
        public int CreateEntity () {
            var entity = CreateEntityInternal ();
            AddDelayedUpdate (DelayedUpdate.Op.SafeRemoveEntity, entity, null, -1);
            return entity;
        }

        /// <summary>
        /// Creates new entity and adds component to it.
        /// Slightly faster than CreateEntity() + AddComponent() sequence.
        /// </summary>
        public T CreateEntityWith<T> () where T : class, new () {
            T component;
            CreateEntityWith<T> (out component);
            return component;
        }

        /// <summary>
        /// Creates new entity and adds component to it.
        /// Slightly faster than CreateEntity() + AddComponent() sequence.
        /// </summary>
        /// <param name="component">Added component of type T.</param>
        /// <returns>New entity Id.</returns>
        public int CreateEntityWith<T> (out T component) where T : class, new () {
            var entity = CreateEntityInternal ();
            component = AddComponent<T> (entity);
            return entity;
        }

        /// <summary>
        /// Creates new entity and adds component to it.
        /// Slightly faster than CreateEntity() + AddComponent() sequence.
        /// </summary>
        /// <param name="c1">Added component of type T1.</param>
        /// <param name="c2">Added component of type T2.</param>
        /// <returns>New entity Id.</returns>
        public int CreateEntityWith<T1, T2> (out T1 c1, out T2 c2) where T1 : class, new () where T2 : class, new () {
#if DEBUG
            if (typeof (T1) == typeof (T2)) { throw new Exception (string.Format ("Cant create entity with multiple components of same type \"{0}\"", typeof (T2).Name)); }
#endif
            var entity = CreateEntityInternal ();
            c1 = AddComponent<T1> (entity);
            c2 = AddComponent<T2> (entity);
            return entity;
        }

        /// <summary>
        /// Creates new entity and adds component to it.
        /// Slightly faster than CreateEntity() + AddComponent() sequence.
        /// </summary>
        /// <param name="c1">Added component of type T1.</param>
        /// <param name="c2">Added component of type T2.</param>
        /// <param name="c3">Added component of type T3.</param>
        /// <returns>New entity Id.</returns>
        public int CreateEntityWith<T1, T2, T3> (out T1 c1, out T2 c2, out T3 c3) where T1 : class, new () where T2 : class, new () where T3 : class, new () {
#if DEBUG
            if (typeof (T1) == typeof (T2)) { throw new Exception (string.Format ("Cant create entity with multiple components of same type \"{0}\"", typeof (T1).Name)); }
            if (typeof (T1) == typeof (T3) || typeof (T2) == typeof (T3)) { throw new Exception (string.Format ("Cant create entity with multiple components of same type \"{0}\"", typeof (T3).Name)); }
#endif
            var entity = CreateEntityInternal ();
            c1 = AddComponent<T1> (entity);
            c2 = AddComponent<T2> (entity);
            c3 = AddComponent<T3> (entity);
            return entity;
        }

        /// <summary>
        /// Removes exists entity or throws exception on invalid one.
        /// </summary>
        /// <param name="entity">Entity.</param>
        public void RemoveEntity (int entity) {
#if DEBUG
            if (entity < 0 || entity >= _entitiesCount) { throw new Exception (string.Format ("Invalid entity: {0}", entity)); }
#endif
            if (!_entities[entity].IsReserved) {
                AddDelayedUpdate (DelayedUpdate.Op.RemoveEntity, entity, null, -1);
            }
        }

        /// <summary>
        /// Gets exist one or adds new component to entity.
        /// </summary>
        /// <param name="entity">Entity.</param>
        /// <param name="isNew">Is component was added in this call?</param>
        public T EnsureComponent<T> (int entity, out bool isNew) where T : class, new () {
#if DEBUG
            if (entity < 0 || entity >= _entitiesCount) { throw new Exception (string.Format ("Invalid entity: {0}", entity)); }
            if (_entities[entity].IsReserved) { throw new Exception (string.Format ("\"{0}\" component cant be added to removed entity {1}", typeof (T).Name, entity)); }
#endif
            var entityData = _entities[entity];
            var pool = EcsComponentPool<T>.Instance;
            for (var i = 0; i < entityData.ComponentsCount; i++) {
                if (entityData.Components[i].Pool == pool) {
                    isNew = false;
                    return (T) entityData.Components[i].Pool.GetExistItemById (entityData.Components[i].ItemId);
                }
            }

            var link = new ComponentLink (pool, pool.RequestNewId ());
            if (entityData.ComponentsCount == entityData.Components.Length) {
                Array.Resize (ref entityData.Components, entityData.ComponentsCount << 1);
            }
            entityData.Components[entityData.ComponentsCount++] = link;
            AddDelayedUpdate (DelayedUpdate.Op.AddComponent, entity, pool, link.ItemId);
#if DEBUG
            var dbgCmpt = pool.Items[link.ItemId];
            for (var ii = 0; ii < _debugListeners.Count; ii++) {
                _debugListeners[ii].OnComponentAdded (entity, dbgCmpt);
            }
#endif
#if LEOECS_ENABLE_WORLD_EVENTS
            var evtCmpt = pool.Items[link.ItemId];
            for (var ii = 0; ii < _eventListeners.Count; ii++) {
                _eventListeners[ii].OnComponentAdded (entity, evtCmpt);
            }
#endif
            isNew = true;
            return pool.Items[link.ItemId];
        }

        /// <summary>
        /// Adds component to entity. Will throw exception if component already exists.
        /// </summary>
        /// <param name="entity">Entity.</param>
        public T AddComponent<T> (int entity) where T : class, new () {
#if DEBUG
            if (entity < 0 || entity >= _entitiesCount) { throw new Exception (string.Format ("Invalid entity: {0}", entity)); }
            if (_entities[entity].IsReserved) { throw new Exception (string.Format ("\"{0}\" component cant be added to removed entity {1}", typeof (T).Name, entity)); }
#endif
            var entityData = _entities[entity];
            var pool = EcsComponentPool<T>.Instance;
#if DEBUG
            var i = entityData.ComponentsCount - 1;
            for (; i >= 0; i--) {
                if (entityData.Components[i].Pool == pool) {
                    break;
                }
            }
            if (i != -1) { throw new Exception (string.Format ("\"{0}\" component already exists on entity {1}", typeof (T).Name, entity)); }
#endif
            // create separate filter for one-frame components.
            if (pool.IsOneFrame) {
                if (!_oneFrameFilters.ContainsKey (pool.GetComponentTypeIndex ())) {
                    _oneFrameFilters[pool.GetComponentTypeIndex ()] = GetFilter (typeof (EcsFilter<T>));
                }
            }

            var link = new ComponentLink (pool, pool.RequestNewId ());
            if (entityData.ComponentsCount == entityData.Components.Length) {
                Array.Resize (ref entityData.Components, entityData.ComponentsCount << 1);
            }
            entityData.Components[entityData.ComponentsCount++] = link;

            AddDelayedUpdate (DelayedUpdate.Op.AddComponent, entity, pool, link.ItemId);
#if DEBUG
            var dbgCmpt = pool.Items[link.ItemId];
            for (var ii = 0; ii < _debugListeners.Count; ii++) {
                _debugListeners[ii].OnComponentAdded (entity, dbgCmpt);
            }
#endif
#if LEOECS_ENABLE_WORLD_EVENTS
            var evtCmpt = pool.Items[link.ItemId];
            for (var ii = 0; ii < _eventListeners.Count; ii++) {
                _eventListeners[ii].OnComponentAdded (entity, evtCmpt);
            }
#endif
            return pool.Items[link.ItemId];
        }

        /// <summary>
        /// Removes component from entity.
        /// </summary>
        /// <param name="entity">Entity.</param>
        /// <param name="noerror">Suppress error if component not exists.</param>
#if NET_4_6 || NET_STANDARD_2_0
        [System.Runtime.CompilerServices.MethodImpl (System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        public void RemoveComponent<T> (int entity, bool noError = false) where T : class, new () {
#if DEBUG
            if (entity < 0 || entity >= _entitiesCount) { throw new Exception (string.Format ("Invalid entity: {0}", entity)); }
#endif
            var entityData = _entities[entity];
            var pool = EcsComponentPool<T>.Instance;
            ComponentLink link;
            link.ItemId = -1;
            var i = entityData.ComponentsCount - 1;
            for (; i >= 0; i--) {
                link = entityData.Components[i];
                if (link.Pool == pool) {
                    break;
                }
            }
            if (noError && i == -1) {
                return;
            }
#if DEBUG
            if (i == -1) { throw new Exception (string.Format ("\"{0}\" component not exists on entity {1}", typeof (T).Name, entity)); }
#endif
            AddDelayedUpdate (DelayedUpdate.Op.RemoveComponent, entity, pool, link.ItemId);
            entityData.ComponentsCount--;
            Array.Copy (entityData.Components, i + 1, entityData.Components, i, entityData.ComponentsCount - i);
        }

        /// <summary>
        /// Gets component on entity.
        /// </summary>
        /// <param name="entity">Entity.</param>
#if NET_4_6 || NET_STANDARD_2_0
        [System.Runtime.CompilerServices.MethodImpl (System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        public T GetComponent<T> (int entity) where T : class, new () {
#if DEBUG
            if (entity < 0 || entity >= _entitiesCount) { throw new Exception (string.Format ("Invalid entity: {0}", entity)); }
            if (_entities[entity].IsReserved) { throw new Exception (string.Format ("\"{0}\" component cant be obtained from removed entity {1}", typeof (T).Name, entity)); }
#endif
            var entityData = _entities[entity];
            var pool = EcsComponentPool<T>.Instance;
            for (var i = 0; i < entityData.ComponentsCount; i++) {
                if (entityData.Components[i].Pool == pool) {
                    return pool.Items[entityData.Components[i].ItemId];
                }
            }
            return null;
        }

        /// <summary>
        /// Gets all components on entity.
        /// </summary>
        /// <param name="entity">Entity.</param>
        /// <param name="list">List to put results in it. if null - will be created.</param>
        /// <returns>Amount of components in list.</returns>
        public int GetComponents (int entity, ref object[] list) {
#if DEBUG
            if (entity < 0 || entity >= _entitiesCount) { throw new Exception (string.Format ("Invalid entity: {0}", entity)); }
#endif
            var entityData = _entities[entity];
            var count = entityData.ComponentsCount;
            if (list == null || list.Length < count) {
                list = new object[entityData.ComponentsCount];
            }
            for (var i = 0; i < count; i++) {
                var link = entityData.Components[i];
                list[i] = link.Pool.GetExistItemById (link.ItemId);
            }
            return count;
        }

        /// <summary>
        /// Returns true if entity exists.
        /// </summary>
        /// <param name="entity">Entity.</param>
        public bool IsEntityExists (int entity) {
            return entity >= 0 && entity < _entitiesCount && !_entities[entity].IsReserved;
        }

        /// <summary>
        /// Gets stats of internal data.
        /// </summary>
        public EcsWorldStats GetStats () {
            var stats = new EcsWorldStats () {
                ActiveEntities = _entitiesCount - _reservedEntitiesCount,
                ReservedEntities = _reservedEntitiesCount,
                Filters = _filtersCount,
                Components = EcsHelpers.ComponentsCount,
                OneFrameComponents = _oneFrameFilters.Count
            };
            return stats;
        }

#if DEBUG
        int _delayedUpdateIteration = 0;
#endif
        /// <summary>
        /// Manually processes delayed updates. Use carefully!
        /// </summary>
        public void ProcessDelayedUpdates () {
            if (_delayedUpdatesCount == 0) {
                return;
            }
#if DEBUG
            _delayedUpdateIteration++;
            if (_delayedUpdateIteration > 5) {
                throw new Exception ("Too deep delayed updates call stack");
            }
#endif
            var iMax = _delayedUpdatesCount;
            for (var i = 0; i < iMax; i++) {
                var op = _delayedUpdates[i];
                var entityData = _entities[op.Entity];
                _delayedOpMask.CopyFrom (entityData.Mask);
                switch (op.Type) {
                    case DelayedUpdate.Op.RemoveEntity:
#if DEBUG
                        if (entityData.IsReserved) { throw new Exception (string.Format ("Entity {0} already removed", op.Entity)); }
#endif
                        while (entityData.ComponentsCount > 0) {
                            var link = entityData.Components[entityData.ComponentsCount - 1];
                            var componentId = link.Pool.GetComponentTypeIndex ();
                            entityData.Mask.SetBit (componentId, false);
#if DEBUG
                            var dbgCmpt1 = link.Pool.GetExistItemById (link.ItemId);
                            for (int ii = 0, iiMax = _debugListeners.Count; ii < iiMax; ii++) {
                                _debugListeners[ii].OnComponentRemoved (op.Entity, dbgCmpt1);
                            }
#endif
#if LEOECS_ENABLE_WORLD_EVENTS
                            var evtCmpt1 = link.Pool.GetExistItemById (link.ItemId);
                            for (int ii = 0, iiMax = _eventListeners.Count; ii < iiMax; ii++) {
                                _eventListeners[ii].OnComponentRemoved (op.Entity, evtCmpt1);
                            }
#endif
                            UpdateFilters (op.Entity, _delayedOpMask, entityData.Mask);
                            link.Pool.RecycleById (link.ItemId);
                            _delayedOpMask.SetBit (componentId, false);
                            entityData.ComponentsCount--;
                        }
                        ReserveEntity (op.Entity, entityData);
                        break;
                    case DelayedUpdate.Op.SafeRemoveEntity:
                        if (!entityData.IsReserved && entityData.ComponentsCount == 0) {
                            ReserveEntity (op.Entity, entityData);
                        }
                        break;
                    case DelayedUpdate.Op.AddComponent:
                        var bit = op.Pool.GetComponentTypeIndex ();
#if DEBUG
                        if (entityData.Mask.GetBit (bit)) { throw new Exception (string.Format ("Cant add component on entity {0}, already marked as added in mask", op.Entity)); }
#endif
                        entityData.Mask.SetBit (bit, true);
                        UpdateFilters (op.Entity, _delayedOpMask, entityData.Mask);
                        break;
                    case DelayedUpdate.Op.RemoveComponent:
                        var bitRemove = op.Pool.GetComponentTypeIndex ();
#if DEBUG
                        if (!entityData.Mask.GetBit (bitRemove)) { throw new Exception (string.Format ("Cant remove component on entity {0}, marked as not exits in mask", op.Entity)); }
                        var dbgCmpt2 = op.Pool.GetExistItemById (op.ComponentId);
                        for (int ii = 0, iiMax = _debugListeners.Count; ii < iiMax; ii++) {
                            _debugListeners[ii].OnComponentRemoved (op.Entity, dbgCmpt2);
                        }
#endif
#if LEOECS_ENABLE_WORLD_EVENTS
                        var evtCmpt2 = op.Pool.GetExistItemById (op.ComponentId);
                        for (int ii = 0, iiMax = _eventListeners.Count; ii < iiMax; ii++) {
                            _eventListeners[ii].OnComponentRemoved (op.Entity, evtCmpt2);
                        }
#endif
                        entityData.Mask.SetBit (bitRemove, false);
                        UpdateFilters (op.Entity, _delayedOpMask, entityData.Mask);
                        op.Pool.RecycleById (op.ComponentId);
                        if (entityData.ComponentsCount == 0) {
                            AddDelayedUpdate (DelayedUpdate.Op.SafeRemoveEntity, op.Entity, null, -1);
                        }
                        break;
                }
            }
            if (_delayedUpdatesCount == iMax) {
                _delayedUpdatesCount = 0;
            } else {
                Array.Copy (_delayedUpdates, iMax, _delayedUpdates, 0, _delayedUpdatesCount - iMax);
                _delayedUpdatesCount -= iMax;
                ProcessDelayedUpdates ();
            }
#if DEBUG
            _delayedUpdateIteration--;
#endif
        }

        /// <summary>
        /// Gets filter with specific include / exclude masks.
        /// </summary>
#if NET_4_6 || NET_STANDARD_2_0
        [System.Runtime.CompilerServices.MethodImpl (System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        public T GetFilter<T> () where T : EcsFilter {
            return GetFilter (typeof (T)) as T;
        }

        /// <summary>
        /// Gets filter with specific include / exclude masks.
        /// </summary>
        /// <param name="filterType">Type of filter.</param>
#if NET_4_6 || NET_STANDARD_2_0
        [System.Runtime.CompilerServices.MethodImpl (System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        public EcsFilter GetFilter (Type filterType) {
#if DEBUG
            if (filterType == null) { throw new Exception ("FilterType is null"); }
            if (!filterType.IsSubclassOf (typeof (EcsFilter))) { throw new Exception (string.Format ("Invalid filter-type: {0}", filterType)); }
#endif
            for (int i = 0, iMax = _filtersCount; i < iMax; i++) {
                if (_filters[i].GetType () == filterType) {
                    return _filters[i];
                }
            }
            var filter = (EcsFilter) Activator.CreateInstance (filterType, true);
            filter.World = this;
#if DEBUG
            for (int j = 0, jMax = _filtersCount; j < jMax; j++) {
                if (_filters[j].IncludeMask.IsEquals (filter.IncludeMask) && _filters[j].ExcludeMask.IsEquals (filter.ExcludeMask)) {
                    throw new Exception (string.Format ("Duplicate filter type \"{0}\": filter type \"{1}\" already has same types in different order.", filterType, _filters[j].GetType ()));
                }
            }
#endif
            if (_filtersCount == _filters.Length) {
                Array.Resize (ref _filters, _filtersCount << 1);
            }
            _filters[_filtersCount++] = filter;

            return filter;
        }

        /// <summary>
        /// Removes specific filter.
        /// Use carefully! Filter will be removed from world without notification of any systems!
        /// </summary>
        public void RemoveFilter<T> () where T : EcsFilter {
            var filterType = typeof (T);
            for (int i = 0, iMax = _filtersCount; i < iMax; i++) {
                if (_filters[i].GetType () == filterType) {
                    _filtersCount--;
                    Array.Copy (_filters, i + 1, _filters, i, _filtersCount - i);
                    break;
                }
            }

            foreach (var pair in _oneFrameFilters) {
                if (pair.Value.GetType () == filterType) {
                    _oneFrameFilters.Remove (pair.Key);
                    break;
                }
            }
        }

        /// <summary>
        /// Removes all components marked with EcsOneFrameAttribute.
        /// </summary>
        public void RemoveOneFrameComponents () {
            foreach (var pair in _oneFrameFilters) {
                var filter = pair.Value;
                if (filter.EntitiesCount > 0) {
                    var pool = filter.GetComponentPool (0);
                    for (int e = 0, eMax = filter.EntitiesCount; e < eMax; e++) {
                        var entityData = _entities[filter.Entities[e]];
                        for (int c = 0, cMax = entityData.ComponentsCount; c < cMax; c++) {
                            if (entityData.Components[c].Pool == pool) {
                                AddDelayedUpdate (DelayedUpdate.Op.RemoveComponent, filter.Entities[e], pool, entityData.Components[c].ItemId);
                                entityData.ComponentsCount--;
                                Array.Copy (entityData.Components, c + 1, entityData.Components, c, entityData.ComponentsCount - c);
                                break;
                            }
                        }
                    }
                }
            }
            ProcessDelayedUpdates ();
        }

        /// <summary>
        /// Create entity with support of re-using reserved instances.
        /// </summary>
#if NET_4_6 || NET_STANDARD_2_0
        [System.Runtime.CompilerServices.MethodImpl (System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        int CreateEntityInternal () {
            int entity;
            if (_reservedEntitiesCount > 0) {
                _reservedEntitiesCount--;
                entity = _reservedEntities[_reservedEntitiesCount];
                _entities[entity].IsReserved = false;
            } else {
                entity = _entitiesCount;
                if (_entitiesCount == _entities.Length) {
                    Array.Resize (ref _entities, _entitiesCount << 1);
                }
                _entities[_entitiesCount++] = new EcsEntity ();
            }
#if DEBUG
            for (var ii = 0; ii < _debugListeners.Count; ii++) {
                _debugListeners[ii].OnEntityCreated (entity);
            }
#endif
#if LEOECS_ENABLE_WORLD_EVENTS
            for (var ii = 0; ii < _eventListeners.Count; ii++) {
                _eventListeners[ii].OnEntityCreated (entity);
            }
#endif
            return entity;
        }

#if NET_4_6 || NET_STANDARD_2_0
        [System.Runtime.CompilerServices.MethodImpl (System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        void AddDelayedUpdate (DelayedUpdate.Op type, int entity, IEcsComponentPool component, int componentId) {
            if (_delayedUpdatesCount == _delayedUpdates.Length) {
                Array.Resize (ref _delayedUpdates, _delayedUpdatesCount << 1);
            }
            _delayedUpdates[_delayedUpdatesCount++] = new DelayedUpdate (type, entity, component, componentId);
        }

        /// <summary>
        /// Puts entity to pool (reserved list) to reuse later.
        /// </summary>
        /// <param name="entity">Entity Id.</param>
        /// <param name="entityData">EcsEntity instance.</param>
#if NET_4_6 || NET_STANDARD_2_0
        [System.Runtime.CompilerServices.MethodImpl (System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        void ReserveEntity (int entity, EcsEntity entityData) {
            entityData.IsReserved = true;
            if (_reservedEntitiesCount == _reservedEntities.Length) {
                Array.Resize (ref _reservedEntities, _reservedEntitiesCount << 1);
            }
            _reservedEntities[_reservedEntitiesCount++] = entity;
#if DEBUG
            for (var ii = 0; ii < _debugListeners.Count; ii++) {
                _debugListeners[ii].OnEntityRemoved (entity);
            }
#endif
#if LEOECS_ENABLE_WORLD_EVENTS
            for (var ii = 0; ii < _eventListeners.Count; ii++) {
                _eventListeners[ii].OnEntityRemoved (entity);
            }
#endif
        }

        /// <summary>
        /// Updates all filters for changed component mask.
        /// </summary>
        /// <param name="entity">Entity.</param>
        /// <param name="oldMask">Old component state.</param>
        /// <param name="newMask">New component state.</param>
#if NET_4_6 || NET_STANDARD_2_0
        [System.Runtime.CompilerServices.MethodImpl (System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        void UpdateFilters (int entity, EcsComponentMask oldMask, EcsComponentMask newMask) {
            for (var i = _filtersCount - 1; i >= 0; i--) {
                var filter = _filters[i];
                var isNewMaskCompatible = newMask.IsCompatible (filter);
                if (oldMask.IsCompatible (filter)) {
                    if (!isNewMaskCompatible) {
#if DEBUG
                        var ii = filter.EntitiesCount - 1;
                        for (; ii >= 0; ii--) {
                            if (filter.Entities[ii] == entity) {
                                break;
                            }
                        }
                        if (ii == -1) { throw new Exception (string.Format ("Something wrong - entity {0} should be in filter {1}, but not exits.", entity, filter)); }
#endif
                        filter.RaiseOnRemoveEvent (entity);
                    }
                } else {
                    if (isNewMaskCompatible) {
                        filter.RaiseOnAddEvent (entity);
                    }
                }
            }
        }

        [System.Runtime.InteropServices.StructLayout (System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
        struct DelayedUpdate {
            public enum Op : byte {
                RemoveEntity,
                SafeRemoveEntity,
                AddComponent,
                RemoveComponent
            }
            public Op Type;
            public int Entity;
            public IEcsComponentPool Pool;
            public int ComponentId;

            public DelayedUpdate (Op type, int entity, IEcsComponentPool component, int componentId) {
                Type = type;
                Entity = entity;
                Pool = component;
                ComponentId = componentId;
            }
        }

        struct ComponentLink {
            public IEcsComponentPool Pool;
            public int ItemId;

            public ComponentLink (IEcsComponentPool pool, int itemId) {
                Pool = pool;
                ItemId = itemId;
            }
        }

        sealed class EcsEntity {
            public bool IsReserved;
            public readonly EcsComponentMask Mask = new EcsComponentMask ();
            public int ComponentsCount;
            public ComponentLink[] Components = new ComponentLink[8];
        }
    }

    /// <summary>
    /// Stats of EcsWorld instance.
    /// </summary>
    public struct EcsWorldStats {
        /// <summary>
        /// Amount of active entities.
        /// </summary>
        public int ActiveEntities;

        /// <summary>
        /// Amount of cached (not in use) entities.
        /// </summary>
        public int ReservedEntities;

        /// <summary>
        /// Amount of registered filters.
        /// </summary>
        public int Filters;

        /// <summary>
        /// Amount of registered component types.
        /// </summary>
        public int Components;

        /// <summary>
        /// Amount of one-frame registered components.
        /// </summary>
        public int OneFrameComponents;
    }
}