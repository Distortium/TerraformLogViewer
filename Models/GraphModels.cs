namespace TerraformLogViewer.Models
{
    using Blazor.Diagrams;
    using Blazor.Diagrams.Core.Geometry;
    using Blazor.Diagrams.Core.Models;

    public class GraphNode : NodeModel
    {
        public TerraformResource Resource { get; set; }
        public string NodeType { get; set; } = "resource";
        public string Status { get; set; } = "unknown";
        public string CustomCssClass { get; set; } = string.Empty;

        public GraphNode(TerraformResource resource, Point? position = null) : base(position ?? new Point(0, 0))
        {
            Resource = resource;
            Status = resource.Status.ToString().ToLower();

            // Добавляем порты для соединений
            AddPort(PortAlignment.Top);
            AddPort(PortAlignment.Right);
            AddPort(PortAlignment.Bottom);
            AddPort(PortAlignment.Left);
        }
    }

    public class GraphLink : LinkModel
    {
        public string RelationshipType { get; set; } = "dependency";
        public int Strength { get; set; } = 1;

        public GraphLink(GraphNode sourceNode, GraphNode targetNode)
            : base(sourceNode, targetNode)
        {
        }

        public GraphLink(PortModel sourcePort, PortModel targetPort)
            : base(sourcePort, targetPort)
        {
        }
    }

    public class GraphLayout
    {
        public BlazorDiagram Diagram { get; set; } = null!;
        public List<GraphNode> Nodes { get; set; } = new();
        public List<GraphLink> Links { get; set; } = new();
        public Dictionary<string, Point> NodePositions { get; set; } = new();
    }

    public class LayoutOptions
    {
        public int HorizontalSpacing { get; set; } = 200;
        public int VerticalSpacing { get; set; } = 120;
        public int LayerSpacing { get; set; } = 100;
        public bool UseHierarchicalLayout { get; set; } = true;
    }
}
