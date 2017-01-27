﻿using System;
using System.Threading;
using NewLife;
using NewLife.Reflection;
using XCode.DataAccessLayer;

namespace XCode.Cache
{
    /// <summary>缓存基类</summary>
    public abstract class CacheBase<TEntity> : CacheBase where TEntity : Entity<TEntity>, new()
    {
        #region 属性
        /// <summary>连接名</summary>
        public String ConnName { get; set; }

        /// <summary>表名</summary>
        public String TableName { get; set; }
        #endregion

        /// <summary>调用委托方法前设置连接名和表名，调用后还原</summary>
        internal TResult Invoke<T, TResult>(Func<T, TResult> callback, T arg)
        {
            var cn = Entity<TEntity>.Meta.ConnName;
            var tn = Entity<TEntity>.Meta.TableName;

            if (cn != ConnName) Entity<TEntity>.Meta.ConnName = ConnName;
            if (tn != TableName) Entity<TEntity>.Meta.TableName = TableName;

            try
            {
                return callback(arg);
            }
            // 屏蔽对象销毁异常
            catch (ObjectDisposedException) { return default(TResult); }
            // 屏蔽线程取消异常
            catch (ThreadAbortException) { return default(TResult); }
            catch (Exception ex)
            {
                // 无效操作，句柄未初始化，不用出现
                if (ex is InvalidOperationException && ex.Message.Contains("句柄未初始化")) return default(TResult);
                if (DAL.Debug) DAL.WriteLog(ex.ToString());
                throw;
            }
            finally
            {
                if (cn != ConnName) Entity<TEntity>.Meta.ConnName = cn;
                if (tn != TableName) Entity<TEntity>.Meta.TableName = tn;
            }
        }
    }

    /// <summary>缓存基类</summary>
    public abstract class CacheBase : DisposeBase
    {
        #region 设置
        /// <summary>是否调试缓存模块</summary>
        public static Boolean Debug { get { return Setting.Current.CacheDebug; } }
        #endregion

        internal static void WriteLog(String format, params Object[] args)
        {
            if (Debug) DAL.WriteLog(format, args);
        }

        /// <summary>检查并显示统计信息</summary>
        /// <param name="next"></param>
        /// <param name="total"></param>
        /// <param name="show"></param>
        internal static void CheckShowStatics(ref DateTime next, ref Int32 total, Action show)
        {
            if (next < DateTime.Now)
            {
                var isfirst = next == DateTime.MinValue;
                next = DAL.Debug ? DateTime.Now.AddMinutes(10) : DateTime.Now.AddHours(24);

                if (!isfirst) show();
            }

            Interlocked.Increment(ref total);
        }
    }
}