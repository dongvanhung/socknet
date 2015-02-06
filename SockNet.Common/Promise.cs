﻿using System;
using System.Threading;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Common
{
    /// <summary>
    /// A fulfiller of promises.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class PromiseFulfiller<T>
    {
        public Promise<T> Promise { get; private set; }

        public PromiseFulfiller(Promise<T> promise)
        {
            this.Promise = promise;
        }

        public void Fulfill(T value)
        {
            Promise.Set(value);
        }

        public void Fulfill(Exception e)
        {
            Promise.Set(e);
        }
    }

    /// <summary>
    /// A promise for data.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Promise<T>
    {
        /// <summary>
        /// Returns true if this promise was fulfilled.
        /// </summary>
        public bool IsFulfilled { get; protected set; }

        public delegate void OnFulfilledDelegate(T value, Exception error, Promise<T> promise);

        /// <summary>
        /// An event that will get invoked when this promise is fulfilled.
        /// </summary>
        private event OnFulfilledDelegate OnFulfilledPrivate;
        public event OnFulfilledDelegate OnFulfilled
        {
            add
            {
                if (IsFulfilled)
                {
                    value(this.value, valueException, this);
                }

                 OnFulfilledPrivate += value;
            }

            remove
            {
                OnFulfilledPrivate -= value;
            }
        }

        private EventWaitHandle valueWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);

        private Exception valueException;
        private T value;
        public T Value
        {
            get
            {
                if (valueException != null)
                {
                    throw valueException;
                }

                return value;
            }
        }

        /// <summary>
        /// Creates a promise.
        /// </summary>
        public Promise() : this(default(T))
        {

        }

        /// <summary>
        /// Creates a promise.
        /// </summary>
        /// <param name="value"></param>
        public Promise(T value)
        {
            valueWaitHandle.Reset();

            this.value = value;

            if (value != null)
            {
                FinalizeFulfillment();
            }
        }

        /// <summary>
        /// Waits for the value - returns null if data wasn't set.
        /// </summary>
        /// <param name="timeSpan"></param>
        /// <returns></returns>
        public T WaitForValue(TimeSpan timeSpan)
        {
            valueWaitHandle.WaitOne(timeSpan);

            return Value;
        }

        /// <summary>
        /// Creates a fulfiller for this promise.
        /// </summary>
        /// <returns></returns>
        public PromiseFulfiller<T> CreateFulfiller()
        {
            return new PromiseFulfiller<T>(this);
        }

        /// <summary>
        /// Sets an error for this promise.
        /// </summary>
        /// <param name="e"></param>
        internal void Set(Exception e)
        {
            this.valueException = e;
            FinalizeFulfillment();
        }

        /// <summary>
        /// Sets an error for this promise.
        /// </summary>
        /// <param name="value"></param>
        internal void Set(T value)
        {
            this.value = value;
            FinalizeFulfillment();
        }

        /// <summary>
        /// Finalizes the fulfilment of this promise.
        /// </summary>
        private void FinalizeFulfillment()
        {
            IsFulfilled = true;
            valueWaitHandle.Set();

            if (OnFulfilledPrivate != null)
            {
                OnFulfilledPrivate(value, valueException, this);
            }
        }
    }
}
