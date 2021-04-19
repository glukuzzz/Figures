using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Test1.Controllers
{
	[ApiController]
	[Route("[controller]")]
	public class FiguresController : ControllerBase
	{
		private readonly ILogger<FiguresController> _logger;
		private readonly IOrderStorage _orderStorage;
		private readonly IFigureStorage _figureStorage;

		public FiguresController(ILogger<FiguresController> logger, IOrderStorage orderStorage, IFigureStorage figureStorage)
		{
			_logger = logger;
			_orderStorage = orderStorage;
			_figureStorage = figureStorage;
		}

		// хотим оформить заказ и получить в ответе его стоимость
		[HttpPost]
		public async Task<IActionResult> Order(Cart cart)
		{
			foreach (var position in cart.Positions)
			{
				if ( !FiguresStorage.CheckIfAvailable(position.Figure, position.Count))
				{
					return new BadRequestResult();
				}
				position.Figure = await _figureStorage.GetFigureById(position.Figure.FigureId);
			}

			var order = new Order
			{
				Positions = cart.Positions.Select(p =>
				{
					
					p.Figure.Validate();
					return p;
				}).ToList()
			};

			foreach (var position in cart.Positions)
			{
				FiguresStorage.Reserve(position.Figure, position.Count);
			}

			var result = await _orderStorage.Save(order);

			return new OkObjectResult(result);
		}
		[HttpPost]
		public async Task<IActionResult> AddFigureToStorage()
        {
			try
			{
				var guid = Guid.NewGuid();
				var stream = Request.Body;
				var data = "";
				stream.Position = 0;
				using (StreamReader reader = new StreamReader(stream))
				{
					data = reader.ReadToEnd();
				}
				if (data.Contains("Radius"))
				{
					var circle = (Circle)JsonConvert.DeserializeObject(data, typeof(Circle));
					await _figureStorage.UpdateOrSaveFigure(circle);
				}
				else
				{
					var polygon = (Polygon)JsonConvert.DeserializeObject(data, typeof(Polygon));
					await _figureStorage.UpdateOrSaveFigure(polygon);
				}

				return new ContentResult { StatusCode = 200, Content = guid.ToString(), ContentType = "text/plain" };
			}
			catch (Exception ex) { return new ContentResult { StatusCode = 500, Content = ex.Message, ContentType = "text/plain" }; }
		}
	}


	internal interface IRedisClient
	{
		int Get(string figure_id);
		void Set(string figure_Id, int current);
	}

	public static class FiguresStorage
	{
		// корректно сконфигурированный и готовый к использованию клиент Редиса
		private static IRedisClient RedisClient { get; }
		public static bool CheckIfAvailable(Figure figure, int count) => RedisClient.Get(figure.FigureId) >= count;
		public static void Reserve(Figure figure, int count)
		{
			var current = RedisClient.Get(figure.FigureId);
			RedisClient.Set(figure.FigureId, current - count);
		}
	}

	public interface IPolygonAreaCalculator
    {
		public  float GetArea(Polygon figure);
    }

    public class CustomPolygonAreaCalculator_Ver1 : IPolygonAreaCalculator
    {
        public float GetArea(Polygon figure)
        {
            throw new NotImplementedException();
        }
    }

	public interface IPolygonVerticesCalculator
	{
		public List<PointF> GetPolygonVertices(Polygon figure);
	}

    public class CustomPolygonVerticesCalculator : IPolygonVerticesCalculator
    {
        public List<PointF> GetPolygonVertices(Polygon figure)
        {
            throw new NotImplementedException();
        }
    }

    public class Position
	{
		public Figure Figure { get; set; }
		public int Count { get; set; }
	}

	public class Cart
	{
		public List<Position> Positions { get; set; }
	}

	public class Order
	{
		public List<Position> Positions { get; set; }

		public decimal GetTotal() =>
			Positions.Select(p => p.Figure switch 
			{
				Polygon fig => (decimal)fig.GetArea() * 1.2m,
				Circle fig => (decimal)fig.GetArea() * 0.9m,
				_ => throw new InvalidOperationException("Error")
			}).Sum();
	}

	public abstract class Figure
	{
		string figure_id = null;
		internal bool validated = false;
		public bool IsValidated { get => validated; } 
		public string FigureId { get
			{
				if (string.IsNullOrEmpty(figure_id)) figure_id = Guid.NewGuid().ToString();
				return figure_id;
			}
		}
		public abstract void Validate();
		public abstract double GetArea();
	}

	public class  PolygonSide
    {
		public float Lenght { get; set; }
		public float Angle { get; set; }

    }

    public partial class Polygon : Figure
    {
		
		List<PolygonSide> Sides { get; set; }
		List<PointF> PolygonVertices { get; set; }
        public override void Validate()
        {
			
			if (Sides.Count() == 3 )
            {
				foreach(var t in Sides) if(t.Lenght > Sides.Sum(x=>x.Lenght)-t.Lenght) throw new InvalidOperationException("Triangle restrictions not met");
				return;
			}
			else if(Sides.Count() == 4 && Sides.First().Lenght > 0 && Sides.First().Angle == Math.PI/2 && Sides.Select(x=> new { Side = x.Lenght, Angle = x.Angle }).Distinct().Count() == 1)
            {
				return;
            }
            else
            {
				try { PolygonVertices = verticesCalculator.GetPolygonVertices(this); } catch { }
				if (PolygonVertices.Count > 2 && Math.Sqrt(Math.Pow((PolygonVertices.First().X - PolygonVertices.Last().X), 2) + Math.Pow((PolygonVertices.First().Y - PolygonVertices.Last().Y), 2)) > 0.0001) throw new Exception("Figure validation failed");
				return;
			}
			throw new Exception("Something gone wrong");
		}
    }

	public partial class Polygon : Figure
    {
		 IPolygonAreaCalculator areaCalculator;
		 IPolygonVerticesCalculator verticesCalculator;
		 public Polygon(List<PolygonSide> Sides, IPolygonAreaCalculator areaCalculator, IPolygonVerticesCalculator verticesCalculator)
        {
			this.Sides = Sides;
			this.areaCalculator = areaCalculator;
			this.verticesCalculator = verticesCalculator;
			Validate();
		}
		public override double GetArea()
		{
			if (!IsValidated) throw new Exception("Not Validated");

			if (Sides.Count() == 3)
			{
				var p = (Sides[0].Lenght + Sides[1].Lenght + Sides[2].Lenght) / 2;
				return Math.Sqrt(p * (p - Sides[0].Lenght) * (p - Sides[1].Lenght) * (p - Sides[2].Lenght));
			}
			else if (Sides.Count() == 4 && Sides.First().Lenght > 0 && Sides.First().Angle == Math.PI / 2 && Sides.Select(x => new { Side = x.Lenght, Angle = x.Angle }).Distinct().Count() == 1)
			{
				return Sides.First().Lenght * Sides.First().Lenght;
			}
			else
				areaCalculator.GetArea(this);
			throw new Exception("Something gone wrong");
		}
		
	}


	public class Circle : Figure
	{
		public Circle(double radius)
        {
			Radius = radius;
			Validate();
        }
		public double Radius { get; set; }
		public override void Validate()
		{
			if (Radius < 0)
				throw new InvalidOperationException("Circle restrictions not met");
			validated = true;
		}
		public override double GetArea()
		{
			if (!IsValidated) throw new Exception("Not Validated");
			return Math.PI * Math.Pow(Radius, 2);
		}
	}
	/*
	public class Triangle : Figure
	{
		public override void Validate()
		{
			bool CheckTriangleInequality(float a, float b, float c) => a < b + c;
			if (CheckTriangleInequality(SideA, SideB, SideC)
				&& CheckTriangleInequality(SideB, SideA, SideC)
				&& CheckTriangleInequality(SideC, SideB, SideA))
				return;
			throw new InvalidOperationException("Triangle restrictions not met");
		}

		public override double GetArea()
		{
			var p = (SideA + SideB + SideC) / 2;
			return Math.Sqrt(p * (p - SideA) * (p - SideB) * (p - SideC));
		}

	}

	public class Square : Figure
	{
		public override void Validate()
		{
			if (SideA < 0)
				throw new InvalidOperationException("Square restrictions not met");

			if (SideA != SideB)
				throw new InvalidOperationException("Square restrictions not met");
		}

		public override double GetArea() => SideA * SideA;
	}
	*/

	public interface IOrderStorage
	{
		// сохраняет оформленный заказ и возвращает сумму
		Task<decimal> Save(Order order);
	}

	public interface IFigureStorage
    {
		Task UpdateOrSaveFigure(Figure figure);
		Task<Figure> GetFigureById(string figure_id);
		Task<Figure> GetFigureByParams(params object[] o);
    }


}
