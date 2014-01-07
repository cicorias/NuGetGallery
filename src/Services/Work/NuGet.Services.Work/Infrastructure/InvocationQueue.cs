﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Queue.Protocol;
using Microsoft.WindowsAzure.Storage.Table;
using NuGet.Services.Configuration;
using NuGet.Services.ServiceModel;
using NuGet.Services.Storage;
using NuGet.Services.Work.Models;

namespace NuGet.Services.Work
{
    public class InvocationQueue
    {
        private SqlConnectionStringBuilder _connectionString;
        private ServiceInstanceName _instanceName;
        private Clock _clock;

        protected InvocationQueue() { }

        public InvocationQueue(Clock clock, ServiceInstanceName instanceName, ConfigurationHub config)
            : this(clock, instanceName, config.Sql.GetConnectionString(KnownSqlServer.Primary)) { }

        public InvocationQueue(Clock clock, ServiceInstanceName instanceName, SqlConnectionStringBuilder connectionString)
            : this()
        {
            _clock = clock;
            _instanceName = instanceName;
            _connectionString = connectionString;
        }

        public virtual Task<InvocationState> Enqueue(string job, string source)
        {
            return Enqueue(job, source, null, TimeSpan.Zero);
        }

        public virtual Task<InvocationState> Enqueue(string job, string source, Dictionary<string, string> payload)
        {
            return Enqueue(job, source, payload, TimeSpan.Zero);
        }

        public virtual async Task<InvocationState> Enqueue(string job, string source, Dictionary<string, string> payload, TimeSpan invisibleFor)
        {
            var invisibleUntil = _clock.UtcNow + invisibleFor;

            string payloadString = null;
            if (payload != null)
            {
                payloadString = InvocationPayloadSerializer.Serialize(payload);
            }

            var row = await ConnectAndExec(
                "work.EnqueueInvocation",
                new
                {
                    Job = job,
                    Source = source,
                    Payload = payloadString,
                    NextVisibleAt = invisibleUntil.UtcDateTime,
                    InstanceName = _instanceName.ToString()
                });
            if (row == null)
            {
                return null;
            }
            return new InvocationState(row);
        }

        /// <summary>
        /// Dequeues the next request, if one is present
        /// </summary>
        /// <param name="invisibleFor">The period of time during which the message is invisble to other clients. The job must be <see cref="Complete"/>d before this time or it will be dispatched again</param>
        public virtual async Task<InvocationState> Dequeue(TimeSpan invisibleFor, CancellationToken token)
        {
            var invisibleUntil = _clock.UtcNow + invisibleFor;
            var row = await ConnectAndExec(
                "work.DequeueInvocation",
                new
                {
                    InstanceName = _instanceName.ToString(),
                    HideUntil = invisibleUntil.UtcDateTime
                });

            if (row == null)
            {
                return null;
            }
            else
            {
                return new InvocationState(row);
            }
        }

        /// <summary>
        /// Acknowledges that the request has completed successfully, removing the message from the queue.
        /// </summary>
        /// <param name="request">The request to acknowledge</param>
        /// <returns>A boolean indicating if the request was successful. Failure indicates that another node has updated the invocation since we last checked</returns>
        public virtual async Task<bool> Complete(InvocationState invocation, ExecutionResult result, string resultMessage, string logUrl)
        {
            // Try to complete the row
            var newVersion = await ConnectAndExec(
                "work.CompleteInvocation",
                new
                {
                    Id = invocation.Id,
                    Version = invocation.CurrentVersion,
                    Result = (int)result,
                    ResultMessage = resultMessage,
                    LogUrl = logUrl,
                    InstanceName = _instanceName.ToString()
                });
            return ProcessResult(invocation, newVersion);
        }

        /// <summary>
        /// Extends the visibility timeout of the request. That is, the time during which the 
        /// queue message is hidden from other clients
        /// </summary>
        /// <param name="request">The request to extend</param>
        /// <param name="duration">The duration from the time of invocation to hide the message</param>
        public virtual async Task<bool> Extend(InvocationState invocation, TimeSpan duration)
        {
            var invisibleUntil = _clock.UtcNow + duration;
            var newVersion = await ConnectAndExec(
                "work.ExtendInvocation",
                new
                {
                    Id = invocation.Id,
                    Version = invocation.CurrentVersion,
                    ExtendTo = invisibleUntil.UtcDateTime,
                    InstanceName = _instanceName.ToString()
                });

            return ProcessResult(invocation, newVersion);
        }

        public virtual async Task<bool> Suspend(InvocationState invocation, Dictionary<string, string> newPayload, TimeSpan suspendFor, string logUrl)
        {
            var suspendUntil = _clock.UtcNow + suspendFor;
            var serializedPayload = InvocationPayloadSerializer.Serialize(newPayload);
            var newVersion = await ConnectAndExec(
                "work.SuspendInvocation",
                new
                {
                    Id = invocation.Id,
                    Version = invocation.CurrentVersion,
                    Payload = serializedPayload,
                    SuspendUntil = suspendUntil.UtcDateTime,
                    LogUrl = logUrl,
                    InstanceName = _instanceName.ToString()
                });
            return ProcessResult(invocation, newVersion);
        }

        public virtual async Task<bool> UpdateStatus(InvocationState invocation, InvocationStatus status, ExecutionResult result)
        {
            var newVersion = await ConnectAndExec(
                "work.SetInvocationStatus",
                new
                {
                    Id = invocation.Id,
                    Version = invocation.CurrentVersion,
                    Status = status,
                    Result = result,
                    InstanceName = _instanceName.ToString()
                });
            return ProcessResult(invocation, newVersion);
        }

        public virtual Task<IEnumerable<InvocationState>> GetAll(InvocationListCriteria criteria)
        {
            switch (criteria)
            {
                case InvocationListCriteria.All:
                    return ConnectAndQuery("SELECT * FROM work.Invocations");
                case InvocationListCriteria.Active:
                    return ConnectAndQuery("SELECT * FROM work.ActiveInvocations");
                case InvocationListCriteria.Completed:
                    return ConnectAndQuery("SELECT * FROM work.Invocations WHERE Complete = 1");
                case InvocationListCriteria.Pending:
                    return ConnectAndQuery(
                        "SELECT * FROM work.ActiveInvocations WHERE [NextVisibleAt] <= @now",
                        new
                        {
                            now = _clock.UtcNow.UtcDateTime
                        });
                case InvocationListCriteria.Hidden:
                    return ConnectAndQuery(
                        "SELECT * FROM work.ActiveInvocations WHERE [NextVisibleAt] > @now",
                        new
                        {
                            now = _clock.UtcNow.UtcDateTime
                        });
                case InvocationListCriteria.Suspended:
                    return ConnectAndQuery(
                        "SELECT * FROM work.ActiveInvocations WHERE [Status] = @status",
                        new
                        {
                            status = InvocationStatus.Suspended
                        });
                case InvocationListCriteria.Executing:
                    return ConnectAndQuery(
                        "SELECT * FROM work.ActiveInvocations WHERE [Status] = @status",
                        new
                        {
                            status = InvocationStatus.Executing
                        });
                default:
                    return Task.FromResult(Enumerable.Empty<InvocationState>());
            }
        }

        public virtual async Task<InvocationStatisticsRecord> GetStatistics()
        {
            return (await GetStatisticsCore("InvocationStatistics")).SingleOrDefault();
        }

        public virtual Task<IEnumerable<InvocationStatisticsRecord>> GetJobStatistics()
        {
            return GetStatisticsCore("JobStatistics");
        }

        public virtual Task<IEnumerable<InvocationStatisticsRecord>> GetInstanceStatistics()
        {
            return GetStatisticsCore("InstanceStatistics");
        }

        public virtual Task<InvocationState> Get(Guid id)
        {
            return ConnectAndQuerySingle(
                "SELECT * FROM work.Invocations WHERE Id = @id",
                new { id });
        }

        private async Task<IEnumerable<InvocationStatisticsRecord>> GetStatisticsCore(string view)
        {
            using (var connection = await Connect())
            {
                // Teeny tiny SQL Injection :)
                return await connection.QueryAsync<InvocationStatisticsRecord>("SELECT * FROM work." + view);
            }
        }

        private bool ProcessResult(InvocationState invocation, InvocationState.InvocationRow result)
        {
            if (result == null)
            {
                return false;
            }
            else
            {
                invocation.Update(result);
                return true;
            }
        }

        private async Task<SqlConnection> Connect()
        {
            var conn = new SqlConnection(_connectionString.ConnectionString);
            await conn.OpenAsync();
            return conn;
        }

        private async Task<InvocationState.InvocationRow> ConnectAndExec(string proc, object parameters)
        {
            using (var connection = await Connect())
            {
                return (await connection.QueryAsync<InvocationState.InvocationRow>(
                    proc,
                    parameters,
                    commandType: CommandType.StoredProcedure))
                    .SingleOrDefault();
            }
        }
        
        private async Task<IEnumerable<InvocationState>> ConnectAndQuery(string sql, object parameters = null)
        {
            using (var connection = await Connect())
            {
                return (await connection.QueryAsync<InvocationState.InvocationRow>(
                    sql,
                    parameters,
                    commandType: CommandType.Text)).Select(r => new InvocationState(r));
            }
        }

        private async Task<InvocationState> ConnectAndQuerySingle(string sql, object parameters = null)
        {
            return (await ConnectAndQuery(sql, parameters)).SingleOrDefault();
        }
    }
}