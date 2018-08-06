﻿using Neo.Lux.Cryptography;
using Neo.Lux.Debugger;
using Neo.Lux.Utils;
using Neo.Lux.VM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Neo.Lux.Core
{
    public interface ChainTime
    {
        uint GetTime();
        void AdvanceTime();
    }

    internal class RealTime : ChainTime
    {
        public uint GetTime()
        {
            return DateTime.UtcNow.ToTimestamp();
        }

        public void AdvanceTime()
        {

        }
    }

    internal class SimulatedTime : ChainTime
    {
        public uint Time;

        public SimulatedTime()
        {
            this.Time = DateTime.UtcNow.ToTimestamp();
        }

        public uint GetTime()
        {
            return Time;
        }

        public void AdvanceTime()
        {
            this.Time += 15;
        }
    }

    public class VirtualChain : Chain
    {
        public readonly ChainTime Time;

        public bool HasDebugger => _debugger != null;

        private DebugClient _debugger;

        public VirtualChain(NeoAPI api, KeyPair owner, ChainTime time = null) 
        {
            Reset();

            if (time == null)
            {
                time = new RealTime();
            }

            this.Time = time;

            var scripthash = new UInt160(owner.signatureHash.ToArray());

            var txs = new List<Transaction>();
            foreach (var entry in _assetMap)
            {
                var assetID = entry.Key;
                var tx = new Transaction();
                tx.outputs = new Transaction.Output[] { new Transaction.Output() { assetID = assetID, scriptHash = scripthash, value = 1000000000 } };
                tx.inputs = new Transaction.Input[] { };
                txs.Add(tx);
            }
            GenerateBlock(txs);
        }

        protected override void Reset()
        {
            base.Reset();

            foreach (var entry in NeoAPI.Assets)
            {
                var symbol = entry.Key;
                var assetID = NeoAPI.GetAssetID(symbol);
                _assetMap[assetID] = new Asset() { hash = new UInt256(assetID), name = symbol };
            }
        }

        public void AttachDebugger(DebugClient debugger)
        {
            this._debugger = debugger;
        }

        public void DettachDebugger()
        {
            this._debugger = null;
        }

        internal override void OnLoadScript(ExecutionEngine vm, byte[] script)
        {
            var context = vm.CallingContext;
            if (context != null)
            {
                var sb = new StringBuilder();
                for (int i=0; i<context.EvaluationStack.Count; i++)
                {
                    var item = context.EvaluationStack.Peek(i);
                    if (sb.Length > 0) { sb.Append(","); };
                    sb.Append(FormattingUtils.StackItemAsString(item));
                }
                Logger("Inputs: " + sb.ToString());
            }

            base.OnLoadScript(vm, script);

            if (_debugger != null)
            {
                _debugger.SendScript(script);
            }
        }

        protected override void OnVMStep(ExecutionEngine vm)
        {
            base.OnVMStep(vm);

            if (_debugger != null)
            {
                _debugger.Step(vm);
            }
        }

        private Dictionary<UInt160, UInt160> _witnessMap = new Dictionary<UInt160, UInt160>();
        private Dictionary<UInt160, UInt160> _scriptMap = new Dictionary<UInt160, UInt160>();

        public void BypassKey(UInt160 src, UInt160 dest)
        {
            if (src == dest)
            {
                return;
            }

            Logger($"Mapping witness {src} to use {dest} instead");
            _witnessMap[dest] = src;
        }

        public void BypassScript(UInt160 src, UInt160 dest)
        {
            if (src == dest)
            {
                return;
            }

            Logger($"Mapping script {src} to use {dest} instead");
            _scriptMap[src] = dest;
        }

        protected override bool ValidateWitness(UInt160 a, UInt160 b)
        {
            if (_witnessMap.ContainsKey(a) && _witnessMap[a] == b){
                Logger($"Bypassed witness {a}, using {b} instead");
                return true;
            }

            return base.ValidateWitness(a, b);
        }

        public override byte[] GetScript(byte[] script_hash)
        {
            var hash = new UInt160(script_hash);
            if (_scriptMap.ContainsKey(hash))
            {
                var target = _scriptMap[hash];
                Logger($"Bypassed script {hash}, using {target} instead");
                script_hash = target.ToArray();
            }
            return base.GetScript(script_hash);
        }

        protected override uint GetTime()
        {
            return this.Time.GetTime();
        }

        public bool GenerateBlock(IEnumerable<Transaction> transactions)
        {
            var block = new Block();

            var rnd = new Random();
            block.ConsensusData = ((UInt64)rnd.Next() << 32) + (UInt64)rnd.Next();
            block.Height = (uint)_blocks.Count;
            block.PreviousHash = _blocks.Count > 0 ? _blocks[(uint)(_blocks.Count - 1)].Hash : null;
            //block.MerkleRoot = 
            block.Timestamp = this.Time.GetTime();
            //block.Validator = 0;
            block.Version = 0;
            block.transactions = new Transaction[transactions.Count()];

            int index = 0;
            foreach (var tx in transactions)
            {
                block.transactions[index] = tx;
                index++;
            }

            if (!AddBlock(block))
            {
                return false;
            }

            this.Time.AdvanceTime();

            return true;
        }

    }
}
