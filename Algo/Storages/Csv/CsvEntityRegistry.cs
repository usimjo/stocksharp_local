namespace StockSharp.Algo.Storages.Csv
{
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Globalization;
	using System.IO;
	using System.Linq;
	using System.Text;
	using System.Threading;

	using Ecng.Collections;
	using Ecng.Common;
	using Ecng.Serialization;

	using MoreLinq;

	using StockSharp.BusinessEntities;
	using StockSharp.Localization;
	using StockSharp.Logging;
	using StockSharp.Messages;

	/// <summary>
	/// The CSV storage of trading objects.
	/// </summary>
	public class CsvEntityRegistry : IEntityRegistry
	{
		private class FakeStorage : IStorage
		{
			private readonly CsvEntityRegistry _registry;

			public FakeStorage(CsvEntityRegistry registry)
			{
				_registry = registry;
			}

			public long GetCount<TEntity>()
			{
				return 0;
			}

			public TEntity Add<TEntity>(TEntity entity)
			{
				Added?.Invoke(entity);
				return entity;
			}

			public TEntity GetBy<TEntity>(SerializationItemCollection by)
			{
				return _registry.Securities.ReadById(by[0].Value).To<TEntity>();
				//throw new NotSupportedException();
			}

			public TEntity GetById<TEntity>(object id)
			{
				throw new NotSupportedException();
			}

			public IEnumerable<TEntity> GetGroup<TEntity>(long startIndex, long count, Field orderBy, ListSortDirection direction)
			{
				throw new NotSupportedException();
			}

			public TEntity Update<TEntity>(TEntity entity)
			{
				Updated?.Invoke(entity);
				return entity;
			}

			public void Remove<TEntity>(TEntity entity)
			{
				Removed?.Invoke(entity);
			}

			public void Clear<TEntity>()
			{
			}

			public void ClearCache()
			{
			}

			public BatchContext BeginBatch()
			{
				return new BatchContext(this);
			}

			public void CommitBatch()
			{
			}

			public void EndBatch()
			{
			}

			public event Action<object> Added;

			public event Action<object> Updated;

			public event Action<object> Removed;
		}

		sealed class ExchangeCsvList : CsvEntityList<Exchange>
		{
			public ExchangeCsvList(CsvEntityRegistry registry)
				: base(registry, "exchange.csv")
			{
			}

			protected override object GetKey(Exchange item)
			{
				return item.Name;
			}

			protected override Exchange Read(FastCsvReader reader)
			{
				var board = new Exchange
				{
					Name = reader.ReadString(),
					CountryCode = reader.ReadNullableEnum<CountryCodes>(),
					EngName = reader.ReadString(),
					RusName = reader.ReadString(),
					//ExtensionInfo = Deserialize<Dictionary<object, object>>(reader.ReadString())
				};

				return board;
			}

			protected override void Write(CsvFileWriter writer, Exchange data)
			{
				writer.WriteRow(new[]
				{
					data.Name,
					data.CountryCode.To<string>(),
					data.EngName,
					data.RusName,
					//Serialize(data.ExtensionInfo)
				});
			}
		}

		sealed class ExchangeBoardCsvList : CsvEntityList<ExchangeBoard>
		{
			public ExchangeBoardCsvList(CsvEntityRegistry registry)
				: base(registry, "exchangeboard.csv")
			{
			}

			protected override object GetKey(ExchangeBoard item)
			{
				return item.Code;
			}

			private Exchange GetExchange(string exchangeCode)
			{
				var exchange = Registry.Exchanges.ReadById(exchangeCode);

				if (exchange == null)
					throw new InvalidOperationException(LocalizedStrings.Str1217Params.Put(exchangeCode));

				return exchange;
			}

			protected override ExchangeBoard Read(FastCsvReader reader)
			{
				var board = new ExchangeBoard
				{
					Code = reader.ReadString(),
					Exchange = GetExchange(reader.ReadString()),
					ExpiryTime = reader.ReadString().ToTime(),
					//IsSupportAtomicReRegister = reader.ReadBool(),
					//IsSupportMarketOrders = reader.ReadBool(),
					TimeZone = TimeZoneInfo.FindSystemTimeZoneById(reader.ReadString()),
					WorkingTime =
					{
						Periods = Deserialize<List<WorkingTimePeriod>>(reader.ReadString()),
						SpecialWorkingDays = Deserialize<List<DateTime>>(reader.ReadString()),
						SpecialHolidays = Deserialize<List<DateTime>>(reader.ReadString())
					},
					//ExtensionInfo = Deserialize<Dictionary<object, object>>(reader.ReadString())
				};

				return board;
			}

			protected override void Write(CsvFileWriter writer, ExchangeBoard data)
			{
				writer.WriteRow(new[]
				{
					data.Code,
					data.Exchange.Name,
					data.ExpiryTime.WriteTime(),
					//data.IsSupportAtomicReRegister.To<string>(),
					//data.IsSupportMarketOrders.To<string>(),
					data.TimeZone.Id,
					Serialize(data.WorkingTime.Periods),
					Serialize(data.WorkingTime.SpecialWorkingDays),
					Serialize(data.WorkingTime.SpecialHolidays),
					//Serialize(data.ExtensionInfo)
				});
			}

			private readonly SynchronizedDictionary<object, object> _serializers = new SynchronizedDictionary<object, object>();

			private string Serialize<TItem>(TItem item)
			{
				if (item == null)
					return null;

				var serializer = (XmlSerializer<TItem>)_serializers.SafeAdd(typeof(TItem), k => new XmlSerializer<TItem>());

				using (var stream = new MemoryStream())
				{
					serializer.Serialize(item, stream);
					return Registry.Encoding.GetString(stream.ToArray()).Remove(Environment.NewLine).Replace("\"", "'");
				}
			}

			private TItem Deserialize<TItem>(string value)
				where TItem : class
			{
				if (value.IsEmpty())
					return null;

				var serializer = (XmlSerializer<TItem>)_serializers.SafeAdd(typeof(TItem), k => new XmlSerializer<TItem>());
				var bytes = Registry.Encoding.GetBytes(value.Replace("'", "\""));

				using (var stream = new MemoryStream(bytes))
					return serializer.Deserialize(stream);
			}
		}

		sealed class SecurityCsvList : CsvEntityList<Security>, IStorageSecurityList
		{
			public SecurityCsvList(CsvEntityRegistry registry)
				: base(registry, "security.csv")
			{
				((ICollectionEx<Security>)this).AddedRange += s => _added?.Invoke(s);
				((ICollectionEx<Security>)this).RemovedRange += s => _removed?.Invoke(s);
			}

			#region IStorageSecurityList

			public void Dispose()
			{
			}

			private Action<IEnumerable<Security>> _added;

			event Action<IEnumerable<Security>> ISecurityProvider.Added
			{
				add { _added += value; }
				remove { _added -= value; }
			}

			private Action<IEnumerable<Security>> _removed;

			event Action<IEnumerable<Security>> ISecurityProvider.Removed
			{
				add { _removed += value; }
				remove { _removed -= value; }
			}

			public IEnumerable<Security> Lookup(Security criteria)
			{
				if (criteria.IsLookupAll())
					return ToArray();

				if (criteria.Id.IsEmpty())
					return this.Filter(criteria);

				var security = ((IStorageSecurityList)this).ReadById(criteria.Id);
				return security == null ? Enumerable.Empty<Security>() : new[] { security };
			}

			public void Delete(Security security)
			{
				Remove(security);
			}

			public void DeleteBy(Security criteria)
			{
				this.Filter(criteria).ForEach(s => Remove(s));
			}

			public IEnumerable<string> GetSecurityIds()
			{
				return this.Select(s => s.Id);
			}

			#endregion

			#region CsvEntityList

			protected override object GetKey(Security item)
			{
				return item.Id;
			}

			private ExchangeBoard GetBoard(string boardCode)
			{
				var board = Registry.ExchangeBoards.ReadById(boardCode);

				if (board == null)
					throw new InvalidOperationException(LocalizedStrings.Str1217Params.Put(boardCode));

				return board;
			}

			protected override Security Read(FastCsvReader reader)
			{
				var security = new Security
				{
					Id = reader.ReadString(),
					Name = reader.ReadString(),
					Code = reader.ReadString(),
					Class = reader.ReadString(),
					ShortName = reader.ReadString(),
					Board = GetBoard(reader.ReadString()),
					UnderlyingSecurityId = reader.ReadString(),
					PriceStep = reader.ReadNullableDecimal(),
					VolumeStep = reader.ReadNullableDecimal(),
					Multiplier = reader.ReadNullableDecimal(),
					Decimals = reader.ReadNullableInt(),
					Type = reader.ReadNullableEnum<SecurityTypes>(),
					ExpiryDate = ReadNullableDateTime(reader),
					SettlementDate = ReadNullableDateTime(reader),
					Strike = reader.ReadNullableDecimal(),
					OptionType = reader.ReadNullableEnum<OptionTypes>(),
					Currency = reader.ReadNullableEnum<CurrencyTypes>(),
					ExternalId = new SecurityExternalId
					{
						Sedol = reader.ReadString(),
						Cusip = reader.ReadString(),
						Isin = reader.ReadString(),
						Ric = reader.ReadString(),
						Bloomberg = reader.ReadString(),
						IQFeed = reader.ReadString(),
						InteractiveBrokers = reader.ReadNullableInt(),
						Plaza = reader.ReadString()
					},
					//ExtensionInfo = Deserialize<Dictionary<object, object>>(reader.ReadString())
				};

				return security;
			}

			protected override void Write(CsvFileWriter writer, Security data)
			{
				writer.WriteRow(new[]
				{
					data.Id,
					data.Name,
					data.Code,
					data.Class,
					data.ShortName,
					data.Board.Code,
					data.UnderlyingSecurityId,
					data.PriceStep.To<string>(),
					data.VolumeStep.To<string>(),
					data.Multiplier.To<string>(),
					data.Decimals.To<string>(),
					data.Type.To<string>(),
					data.ExpiryDate?.UtcDateTime.ToString(_dateTimeFormat),
					data.SettlementDate?.UtcDateTime.ToString(_dateTimeFormat),
					data.Strike.To<string>(),
					data.OptionType.To<string>(),
					data.Currency.To<string>(),
					data.ExternalId.Sedol,
					data.ExternalId.Cusip,
					data.ExternalId.Isin,
					data.ExternalId.Ric,
					data.ExternalId.Bloomberg,
					data.ExternalId.IQFeed,
					data.ExternalId.InteractiveBrokers.To<string>(),
					data.ExternalId.Plaza,
					//Serialize(data.ExtensionInfo)
				});
			}

			#endregion
		}

		sealed class PortfolioCsvList : CsvEntityList<Portfolio>
		{
			public PortfolioCsvList(CsvEntityRegistry registry)
				: base(registry, "portfolio.csv")
			{
			}

			protected override object GetKey(Portfolio item)
			{
				return item.Name;
			}

			protected override Portfolio Read(FastCsvReader reader)
			{
				var portfolio = new Portfolio
				{
					Name = reader.ReadString(),
					Board = GetBoard(reader.ReadString()),
					Leverage = reader.ReadNullableDecimal(),
					BeginValue = reader.ReadNullableDecimal(),
					CurrentValue = reader.ReadNullableDecimal(),
					BlockedValue = reader.ReadNullableDecimal(),
					VariationMargin = reader.ReadNullableDecimal(),
					Commission = reader.ReadNullableDecimal(),
					Currency = reader.ReadNullableEnum<CurrencyTypes>(),
					State = reader.ReadNullableEnum<PortfolioStates>(),
					Description = reader.ReadString(),
					LastChangeTime = _dateTimeParser.Parse(reader.ReadString()).ChangeKind(DateTimeKind.Utc),
					LocalTime = _dateTimeParser.Parse(reader.ReadString()).ChangeKind(DateTimeKind.Utc)
				};

				return portfolio;
			}

			private ExchangeBoard GetBoard(string boardCode)
			{
				if (boardCode.IsEmpty())
					return null;

				var board = Registry.ExchangeBoards.ReadById(boardCode);

				if (board == null)
					throw new InvalidOperationException(LocalizedStrings.Str1217Params.Put(boardCode));

				return board;
			}

			protected override void Write(CsvFileWriter writer, Portfolio data)
			{
				writer.WriteRow(new[]
				{
					data.Name,
					data.Board?.Code,
					data.Leverage.To<string>(),
					data.BeginValue.To<string>(),
					data.CurrentValue.To<string>(),
					data.BlockedValue.To<string>(),
					data.VariationMargin.To<string>(),
					data.Commission.To<string>(),
					data.Currency.To<string>(),
					data.State.To<string>(),
					data.Description,
					data.LastChangeTime.UtcDateTime.ToString(_dateTimeFormat),
					data.LocalTime.UtcDateTime.ToString(_dateTimeFormat)
				});
			}
		}

		sealed class PositionCsvList : CsvEntityList<Position>, IStoragePositionList
		{
			public PositionCsvList(CsvEntityRegistry registry)
				: base(registry, "position.csv")
			{
			}

			protected override object GetKey(Position item)
			{
				return Tuple.Create(item.Portfolio, item.Security);
			}

			private Portfolio GetPortfolio(string id)
			{
				var portfolio = Registry.Portfolios.ReadById(id);

				if (portfolio == null)
					throw new InvalidOperationException(LocalizedStrings.Str3622Params.Put(id));

				return portfolio;
			}

			private Security GetSecurity(string id)
			{
				var security = Registry.Securities.ReadById(id);

				if (security == null)
					throw new InvalidOperationException(LocalizedStrings.Str704Params.Put(id));

				return security;
			}

			protected override Position Read(FastCsvReader reader)
			{
				var pfName = reader.ReadString();
				var secId = reader.ReadString();

				var position = new Position
				{
					Portfolio = GetPortfolio(pfName),
					Security = GetSecurity(secId),
					DepoName = reader.ReadString(),
					LimitType = reader.ReadNullableEnum<TPlusLimits>(),
					BeginValue = reader.ReadNullableDecimal(),
					CurrentValue = reader.ReadNullableDecimal(),
					BlockedValue = reader.ReadNullableDecimal(),
					VariationMargin = reader.ReadNullableDecimal(),
					Commission = reader.ReadNullableDecimal(),
					Currency = reader.ReadNullableEnum<CurrencyTypes>(),
					LastChangeTime = _dateTimeParser.Parse(reader.ReadString()).ChangeKind(DateTimeKind.Utc),
					LocalTime = _dateTimeParser.Parse(reader.ReadString()).ChangeKind(DateTimeKind.Utc)
				};

				if (position.Security == null)
					throw new InvalidOperationException(LocalizedStrings.Str1218Params.Put(secId));

				if (position.Portfolio == null)
					throw new InvalidOperationException(LocalizedStrings.Str891);

				return position;
			}

			protected override void Write(CsvFileWriter writer, Position data)
			{
				writer.WriteRow(new[]
				{
					data.Portfolio.Name,
					data.Security.Id,
					data.DepoName,
					data.LimitType.To<string>(),
					data.BeginValue.To<string>(),
					data.CurrentValue.To<string>(),
					data.BlockedValue.To<string>(),
					data.VariationMargin.To<string>(),
					data.Commission.To<string>(),
					data.Description,
					data.LastChangeTime.UtcDateTime.ToString(_dateTimeFormat),
					data.LocalTime.UtcDateTime.ToString(_dateTimeFormat)
				});
			}

			public Position ReadBySecurityAndPortfolio(Security security, Portfolio portfolio)
			{
				return ((IStoragePositionList)this).ReadById(Tuple.Create(portfolio, security));
			}
		}

		private const string _dateTimeFormat = "yyyyMMddHHmmss";
		private static readonly FastDateTimeParser _dateTimeParser = new FastDateTimeParser(_dateTimeFormat);

		private static DateTimeOffset? ReadNullableDateTime(FastCsvReader reader)
		{
			var str = reader.ReadString();

			if (str == null)
				return null;

			return _dateTimeParser.Parse(str).ChangeKind(DateTimeKind.Utc);
		}

		private readonly TimeSpan _flushInterval = TimeSpan.FromSeconds(1);
		private readonly SyncObject _syncRoot = new SyncObject();

		private readonly ExchangeCsvList _exchanges;
		private readonly ExchangeBoardCsvList _exchangeBoards;
		private readonly SecurityCsvList _securities;
		private readonly PortfolioCsvList _portfolios;
		private readonly PositionCsvList _positions;

		private readonly List<IList> _csvLists = new List<IList>();

		private DelayAction _delayAction;
		private Timer _flushTimer;
		private bool _isFlushing;

		/// <summary>
		/// The path to data directory.
		/// </summary>
		public string Path { get; set; }

		/// <summary>
		/// The special interface for direct access to the storage.
		/// </summary>
		public IStorage Storage { get; }

		private Encoding _encoding = Encoding.UTF8;

		/// <summary>
		/// Encoding.
		/// </summary>
		public Encoding Encoding
		{
			get { return _encoding; }
			set
			{
				if (value == null)
					throw new ArgumentNullException(nameof(value));

				_encoding = value;
			}
		}

		/// <summary>
		/// The time delayed action.
		/// </summary>
		public DelayAction DelayAction
		{
			get { return _delayAction; }
			set
			{
				_delayAction = value;

				_exchanges.DelayAction = _delayAction;
				_exchangeBoards.DelayAction = _delayAction;
				_securities.DelayAction = _delayAction;
				_positions.DelayAction = _delayAction;
				_portfolios.DelayAction = _delayAction;
			}
		}

		/// <summary>
		/// List of exchanges.
		/// </summary>
		public IStorageEntityList<Exchange> Exchanges => _exchanges;

		/// <summary>
		/// The list of stock boards.
		/// </summary>
		public IStorageEntityList<ExchangeBoard> ExchangeBoards => _exchangeBoards;

		/// <summary>
		/// The list of instruments.
		/// </summary>
		public IStorageSecurityList Securities => _securities;

		/// <summary>
		/// The list of portfolios.
		/// </summary>
		public IStorageEntityList<Portfolio> Portfolios => _portfolios;

		/// <summary>
		/// The list of positions.
		/// </summary>
		public IStoragePositionList Positions => _positions;

		/// <summary>
		/// Initializes a new instance of the <see cref="CsvEntityRegistry"/>.
		/// </summary>
		/// <param name="path">The path to data directory.</param>
		public CsvEntityRegistry(string path)
		{
			if (path == null)
				throw new ArgumentNullException(nameof(path));

			Path = path;
			Storage = new FakeStorage(this);

			Add(_exchanges = new ExchangeCsvList(this));
			Add(_exchangeBoards = new ExchangeBoardCsvList(this));
			Add(_securities = new SecurityCsvList(this));
			Add(_portfolios = new PortfolioCsvList(this));
			Add(_positions = new PositionCsvList(this));
		}

		/// <summary>
		/// Add list of trade objects.
		/// </summary>
		/// <typeparam name="T">Entity type.</typeparam>
		/// <param name="list">List of trade objects.</param>
		public void Add<T>(CsvEntityList<T> list)
			where T : class
		{
			if (list == null)
				throw new ArgumentNullException(nameof(list));

			_csvLists.Add(list);
		}

		/// <summary>
		/// Initialize the storage.
		/// </summary>
		public void Init()
		{
			Directory.CreateDirectory(Path);

			var errors = new List<Exception>();

			foreach (dynamic list in _csvLists)
			{
				try
				{
					list.ReadItems(errors);
				}
				catch (Exception ex)
				{
					errors.Add(ex);
				}
			}

			if (errors.Count > 0)
				throw new AggregateException(errors);
		}

		internal void TryCreateTimer()
		{
			lock (_syncRoot)
			{
				if (_isFlushing || _flushTimer != null)
					return;

				_flushTimer = ThreadingHelper
					.Timer(() =>
					{
						try
						{
							CultureInfo.InvariantCulture.DoInCulture(OnFlush);
						}
						catch (Exception ex)
						{
							ex.LogError("Flush CSV entity registry error: {0}");
						}
					})
					.Interval(_flushInterval);
			}
		}

		private void OnFlush()
		{
			lock (_syncRoot)
			{
				if (_isFlushing)
					return;

				_isFlushing = true;
			}

			var canStop = true;

			try
			{
				foreach (dynamic list in _csvLists)
					canStop &= list.Flush();
			}
			finally
			{
				lock (_syncRoot)
				{
					_isFlushing = false;

					if (canStop)
					{
						if (_flushTimer != null)
						{
							_flushTimer.Dispose();
							_flushTimer = null;
						}
					}
				}
			}
		}
	}
}