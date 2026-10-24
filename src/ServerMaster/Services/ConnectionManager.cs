using System;
using System.Collections.Generic;
using ServerMaster.Models;
using ServerMaster.Rdp;

namespace ServerMaster.Services
{
    /// <summary>
    /// 会话生命周期管理（UI 线程调用）：
    /// 建立服务器→会话映射、批量连接/断开、去重（一服务器一路）。
    /// </summary>
    public class ConnectionManager
    {
        private readonly List<RdpSession> _sessions = new List<RdpSession>();

        public event Action<RdpSession> SessionAdded;
        public event Action<RdpSession> SessionRemoved;

        public IList<RdpSession> Sessions { get { return _sessions; } }

        public RdpSession FindByServer(ServerInfo server)
        {
            foreach (RdpSession s in _sessions)
                if (s.Server.Id == server.Id) return s;
            return null;
        }

        /// <summary>创建会话（若已存在则返回既有会话）。</summary>
        public RdpSession Create(ServerInfo server)
        {
            RdpSession existing = FindByServer(server);
            if (existing != null) return existing;

            RdpSession session = new RdpSession(server);
            _sessions.Add(session);
            if (SessionAdded != null) SessionAdded(session);
            return session;
        }

        public void Close(RdpSession session)
        {
            if (session == null) return;
            if (_sessions.Remove(session))
            {
                if (SessionRemoved != null) SessionRemoved(session);
            }
            session.Disconnect();
            session.Dispose();
        }

        public void CloseAll()
        {
            RdpSession[] all = _sessions.ToArray();
            _sessions.Clear();
            foreach (RdpSession s in all)
            {
                if (SessionRemoved != null) SessionRemoved(s);
                try { s.Disconnect(); } catch { }
                try { s.Dispose(); } catch { }
            }
        }

        public int ConnectedCount
        {
            get
            {
                int n = 0;
                foreach (RdpSession s in _sessions)
                    if (s.State == SessionState.Connected) n++;
                return n;
            }
        }
    }
}
