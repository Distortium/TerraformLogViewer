namespace TerraformLogViewer.Services
{
    // Services/DependencyGraphService.cs

    using Blazor.Diagrams;
    using Blazor.Diagrams.Core;
    using Blazor.Diagrams.Core.Geometry;
    using Blazor.Diagrams.Core.Models;
    using Blazor.Diagrams.Core.Models.Base;
    using Blazor.Diagrams.Core.Options;
    using Blazor.Diagrams.Core.PathGenerators;
    using Blazor.Diagrams.Core.Routers;
    using Blazor.Diagrams.Options;
    using SvgPathProperties;
    using System.Globalization;
    using System.Text;
    using TerraformLogViewer.Models;

    public interface IDependencyGraphService
    {
        BlazorDiagram CreateDiagram();
        GraphLayout BuildGraphLayout(TerraformLog terraformLog, LayoutOptions? options = null);
        void ApplyHierarchicalLayout(GraphLayout layout, LayoutOptions options);
        void ApplyForceDirectedLayout(GraphLayout layout, LayoutOptions options);
        List<GraphNode> FindRootNodes(GraphLayout layout);
        List<GraphNode> FindLeafNodes(GraphLayout layout);
        List<GraphNode> GetResourceDependencies(GraphLayout layout, string resourceAddress);
        List<GraphNode> GetResourceDependents(GraphLayout layout, string resourceAddress);
        void HighlightProblematicPaths(GraphLayout layout);
    }

    public class DependencyGraphService : IDependencyGraphService
    {
        private readonly ILogger<DependencyGraphService> _logger;

        public DependencyGraphService(ILogger<DependencyGraphService> logger)
        {
            _logger = logger;
        }

        public BlazorDiagram CreateDiagram()
        {
            var options = new BlazorDiagramOptions
            {
                AllowMultiSelection = true,
                Zoom =
                {
                    Enabled = true,
                    Inverse = true
                },
                Links =
                {
                    DefaultColor = "#6B7280",
                    DefaultSelectedColor = "#3B82F6",
                    DefaultRouter = new NormalRouter(),
                    DefaultPathGenerator = new SmoothPathGenerator()
                }
            };

            return new BlazorDiagram(options);
        }

        public GraphLayout BuildGraphLayout(TerraformLog terraformLog, LayoutOptions? options = null)
        {
            options ??= new LayoutOptions();

            var layout = new GraphLayout
            {
                Diagram = CreateDiagram()
            };

            try
            {
                // Создаем узлы для всех ресурсов
                CreateNodes(layout, terraformLog.Resources);

                // Создаем связи между ресурсами
                CreateLinks(layout, terraformLog.Resources);

                // Применяем автоматическое размещение
                ApplyHierarchicalLayout(layout, options);

                // Подсвечиваем проблемные пути
                HighlightProblematicPaths(layout);

                _logger.LogInformation("Built graph layout with {NodeCount} nodes and {LinkCount} links",
                    layout.Nodes.Count, layout.Links.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build graph layout");
                throw;
            }

            return layout;
        }

        private void CreateNodes(GraphLayout layout, List<TerraformResource> resources)
        {
            var position = new Point(0, 0);

            foreach (var resource in resources)
            {
                var node = new GraphNode(resource, position)
                {
                    Title = GetNodeTitle(resource),
                    Size = new Size(300, 50),
                    Position = new Point(position.X + 50, position.Y + 100)
                };

                // Устанавливаем цвет в зависимости от статуса
                SetNodeAppearance(node, resource);

                var controls = layout.Diagram.Controls.AddFor(node);
                controls.Add(new NodeInformationControl());

                layout.Nodes.Add(node);
                layout.Diagram.Nodes.Add(node);
                layout.NodePositions[resource.Address] = position;

                position = node.Position; // временное размещение
            }
        }

        private void CreateLinks(GraphLayout layout, List<TerraformResource> resources)
        {
            var resourceDict = resources.ToDictionary(r => r.Address, r => r);
            var nodeDict = layout.Nodes.ToDictionary(n => n.Resource.Address, n => n);

            foreach (var resource in resources)
            {
                var sourceNode = nodeDict[resource.Address];

                // Создаем связи на основе явных зависимостей
                foreach (var dependencyAddress in resource.Dependencies)
                {
                    if (nodeDict.ContainsKey(dependencyAddress))
                    {
                        var targetNode = nodeDict[dependencyAddress];
                        CreateLink(layout, sourceNode, targetNode, "explicit");
                    }
                }

                // Создаем неявные связи на основе ссылок между ресурсами
                CreateImplicitLinks(layout, resource, sourceNode, resourceDict, nodeDict);
            }
        }

        private void CreateImplicitLinks(GraphLayout layout, TerraformResource resource, GraphNode sourceNode,
            Dictionary<string, TerraformResource> resourceDict, Dictionary<string, GraphNode> nodeDict)
        {
            // Анализируем логи для поиска неявных зависимостей
            foreach (var entry in resource.RelatedEntries)
            {
                // Ищем ссылки на другие ресурсы в сообщениях
                var referencedResources = FindReferencedResources(entry.Message, resourceDict.Keys);

                foreach (var referencedAddress in referencedResources)
                {
                    if (referencedAddress != resource.Address && nodeDict.ContainsKey(referencedAddress))
                    {
                        var targetNode = nodeDict[referencedAddress];

                        // Проверяем, существует ли уже такая связь
                        var existingLink = layout.Links.FirstOrDefault(l =>
                            GetSourceNodeId(l) == sourceNode.Id && GetTargetNodeId(l) == targetNode.Id);

                        if (existingLink == null)
                        {
                            CreateLink(layout, sourceNode, targetNode, "implicit");
                        }
                    }
                }
            }
        }

        private void CreateLink(GraphLayout layout, GraphNode sourceNode, GraphNode targetNode, string relationshipType)
        {
            var link = new GraphLink(sourceNode, targetNode)
            {
                RelationshipType = relationshipType
            };

            // Настраиваем внешний вид связи
            SetLinkAppearance(link, relationshipType);

            layout.Links.Add(link);
            layout.Diagram.Links.Add(link);
        }

        public void ApplyHierarchicalLayout(GraphLayout layout, LayoutOptions options)
        {
            if (!layout.Nodes.Any()) return;

            // Находим корневые узлы (без зависимостей)
            var rootNodes = FindRootNodes(layout);
            var visited = new HashSet<string>();
            var layers = new Dictionary<int, List<GraphNode>>();

            // Распределяем узлы по слоям
            foreach (var rootNode in rootNodes)
            {
                AssignLayers(rootNode, 0, layers, visited, layout);
            }

            // Обрабатываем узлы без связей
            var unconnectedNodes = layout.Nodes.Where(n => !visited.Contains(n.Id)).ToList();
            if (unconnectedNodes.Any())
            {
                layers[999] = unconnectedNodes; // Специальный слой для несвязанных узлов
            }

            // Размещаем узлы на диаграмме
            var currentY = 0.0;

            foreach (var layer in layers.OrderBy(l => l.Key))
            {
                var currentX = 0.0;
                var layerHeight = 0.0;

                foreach (var node in layer.Value)
                {
                    node.Position = new Point(currentX, currentY);
                    currentX += options.HorizontalSpacing;
                    layerHeight = Math.Max(layerHeight, GetNodeHeight(node));
                }

                currentY += layerHeight + options.VerticalSpacing;
            }

            // Центрируем диаграмму
            CenterDiagram(layout);
        }

        public void ApplyForceDirectedLayout(GraphLayout layout, LayoutOptions options)
        {
            // Простая реализация force-directed layout
            var nodes = layout.Nodes;
            var links = layout.Links;

            const double repulsionForce = 1000;
            const double attractionForce = 0.1;
            const double damping = 0.9;
            const int iterations = 100;

            // Инициализация случайных позиций
            var random = new Random();
            foreach (var node in nodes)
            {
                if (node.Position == null || (node.Position.X == 0 && node.Position.Y == 0))
                {
                    node.Position = new Point(
                        random.NextDouble() * 1000,
                        random.NextDouble() * 600
                    );
                }
            }

            // Итерации для стабилизации
            for (int i = 0; i < iterations; i++)
            {
                // Вычисляем силы отталкивания
                foreach (var node1 in nodes)
                {
                    foreach (var node2 in nodes)
                    {
                        if (node1 != node2)
                        {
                            var dx = node1.Position.X - node2.Position.X;
                            var dy = node1.Position.Y - node2.Position.Y;
                            var distance = Math.Sqrt(dx * dx + dy * dy);

                            if (distance > 0)
                            {
                                var force = repulsionForce / (distance * distance);
                                var fx = force * dx / distance;
                                var fy = force * dy / distance;

                                // Применяем силу (упрощенно)
                                node1.Position = new Point(
                                    node1.Position.X + fx * 0.01,
                                    node1.Position.Y + fy * 0.01
                                );
                            }
                        }
                    }
                }

                // Вычисляем силы притяжения для связей
                foreach (var link in links)
                {
                    var sourceNode = FindNodeById(layout, GetSourceNodeId(link));
                    var targetNode = FindNodeById(layout, GetTargetNodeId(link));

                    if (sourceNode != null && targetNode != null)
                    {
                        var dx = targetNode.Position.X - sourceNode.Position.X;
                        var dy = targetNode.Position.Y - sourceNode.Position.Y;
                        var distance = Math.Sqrt(dx * dx + dy * dy);

                        if (distance > 0)
                        {
                            var force = attractionForce * distance;
                            var fx = force * dx / distance;
                            var fy = force * dy / distance;

                            sourceNode.Position = new Point(
                                sourceNode.Position.X + fx * 0.01,
                                sourceNode.Position.Y + fy * 0.01
                            );
                            targetNode.Position = new Point(
                                targetNode.Position.X - fx * 0.01,
                                targetNode.Position.Y - fy * 0.01
                            );
                        }
                    }
                }
            }

            CenterDiagram(layout);
        }

        public List<GraphNode> FindRootNodes(GraphLayout layout)
        {
            // Корневые узлы - те, на которые никто не ссылается
            var nodesWithIncomingLinks = layout.Links
                .Select(l => GetTargetNodeId(l))
                .ToHashSet();

            return layout.Nodes
                .Where(n => !nodesWithIncomingLinks.Contains(n.Id))
                .ToList();
        }

        public List<GraphNode> FindLeafNodes(GraphLayout layout)
        {
            // Листовые узлы - те, которые ни на кого не ссылаются
            var nodesWithOutgoingLinks = layout.Links
                .Select(l => GetSourceNodeId(l))
                .ToHashSet();

            return layout.Nodes
                .Where(n => !nodesWithOutgoingLinks.Contains(n.Id))
                .ToList();
        }

        public List<GraphNode> GetResourceDependencies(GraphLayout layout, string resourceAddress)
        {
            var node = layout.Nodes.FirstOrDefault(n => n.Resource.Address == resourceAddress);
            if (node == null) return new List<GraphNode>();

            return layout.Links
                .Where(l => GetTargetNodeId(l) == node.Id)
                .Select(l => FindNodeById(layout, GetSourceNodeId(l)))
                .Where(n => n != null)
                .ToList()!;
        }

        public List<GraphNode> GetResourceDependents(GraphLayout layout, string resourceAddress)
        {
            var node = layout.Nodes.FirstOrDefault(n => n.Resource.Address == resourceAddress);
            if (node == null) return new List<GraphNode>();

            return layout.Links
                .Where(l => GetSourceNodeId(l) == node.Id)
                .Select(l => FindNodeById(layout, GetTargetNodeId(l)))
                .Where(n => n != null)
                .ToList()!;
        }

        public void HighlightProblematicPaths(GraphLayout layout)
        {
            // Подсвечиваем узлы с ошибками
            foreach (var node in layout.Nodes.Where(n => n.Resource.Status == ResourceStatus.Failed))
            {
                SetErrorAppearance(node);

                // Подсвечиваем путь к корню от проблемного узла
                HighlightPathToRoot(node, layout);
            }

            // Подсвечиваем узлы с предупреждениями
            foreach (var node in layout.Nodes.Where(n =>
                     n.Resource.Errors.Any() || n.Resource.Status == ResourceStatus.Failed))
            {
                SetWarningAppearance(node);
            }
        }

        #region Вспомогательные методы

        private void AssignLayers(GraphNode node, int layer, Dictionary<int, List<GraphNode>> layers,
            HashSet<string> visited, GraphLayout layout)
        {
            if (visited.Contains(node.Id)) return;

            visited.Add(node.Id);

            if (!layers.ContainsKey(layer))
                layers[layer] = new List<GraphNode>();

            layers[layer].Add(node);

            // Рекурсивно обрабатываем зависимые узлы
            var dependents = GetResourceDependents(layout, node.Resource.Address);
            foreach (var dependent in dependents)
            {
                AssignLayers(dependent, layer + 1, layers, visited, layout);
            }
        }

        private string GetNodeTitle(TerraformResource resource)
        {
            var shortAddress = resource.Address.Split('.').Last();
            return $"{shortAddress}\n({resource.Type})";
        }

        private void SetNodeAppearance(GraphNode node, TerraformResource resource)
        {
            // Устанавливаем CSS класс в зависимости от статуса
            node.CustomCssClass = $"tf-node tf-status-{resource.Status.ToString().ToLower()}";

            // Дополнительные стили в зависимости от статуса
            switch (resource.Status)
            {
                case ResourceStatus.Success:
                    node.CustomCssClass += " tf-node-success";
                    break;
                case ResourceStatus.Failed:
                    node.CustomCssClass += " tf-node-failed";
                    break;
                case ResourceStatus.InProgress:
                    node.CustomCssClass += " tf-node-inprogress";
                    break;
                case ResourceStatus.Pending:
                    node.CustomCssClass += " tf-node-pending";
                    break;
            }
        }

        private void SetLinkAppearance(GraphLink link, string relationshipType)
        {
            link.Color = relationshipType == "explicit" ? "#3B82F6" : "#9CA3AF";
            link.Width = relationshipType == "explicit" ? 3 : 1;

            if (relationshipType == "implicit")
            {
                // Для пунктирных линий используем SVG stroke-dasharray
                //link.PathGenerator = new CustomPathGenerator("5,5");
            }
        }

        private void SetErrorAppearance(GraphNode node)
        {
            node.CustomCssClass += " tf-node-error";
        }

        private void SetWarningAppearance(GraphNode node)
        {
            node.CustomCssClass += " tf-node-warning";
        }

        private void HighlightPathToRoot(GraphNode problemNode, GraphLayout layout)
        {
            var pathNodes = new HashSet<GraphNode> { problemNode };
            var currentNodes = new List<GraphNode> { problemNode };

            while (currentNodes.Any())
            {
                var nextNodes = new List<GraphNode>();

                foreach (var currentNode in currentNodes)
                {
                    var dependencies = GetResourceDependencies(layout, currentNode.Resource.Address);

                    foreach (var dependency in dependencies)
                    {
                        if (!pathNodes.Contains(dependency))
                        {
                            pathNodes.Add(dependency);
                            nextNodes.Add(dependency);

                            // Подсвечиваем связь
                            var link = layout.Links.FirstOrDefault(l =>
                                GetSourceNodeId(l) == dependency.Id && GetTargetNodeId(l) == currentNode.Id);
                            if (link != null)
                            {
                                link.Color = "#EF4444";
                                link.Width = 3;
                            }
                        }
                    }
                }

                currentNodes = nextNodes;
            }
        }

        private List<string> FindReferencedResources(string message, ICollection<string> allResourceAddresses)
        {
            var referenced = new List<string>();

            foreach (var address in allResourceAddresses)
            {
                if (message.Contains(address, StringComparison.OrdinalIgnoreCase))
                {
                    referenced.Add(address);
                }
            }

            return referenced;
        }

        private double GetNodeHeight(GraphNode node)
        {
            // Базовая высота узла
            return 80;
        }

        private void CenterDiagram(GraphLayout layout)
        {
            if (!layout.Nodes.Any()) return;

            var minX = layout.Nodes.Min(n => n.Position.X);
            var minY = layout.Nodes.Min(n => n.Position.Y);
            var maxX = layout.Nodes.Max(n => n.Position.X);
            var maxY = layout.Nodes.Max(n => n.Position.Y);

            var centerX = (minX + maxX) / 2;
            var centerY = (minY + maxY) / 2;

            // Смещаем все узлы к центру
            foreach (var node in layout.Nodes)
            {
                node.Position = new Point(
                    node.Position.X - centerX + 400,
                    node.Position.Y - centerY + 300
                );
            }
        }

        private string? GetSourceNodeId(LinkModel link)
        {
            return link.Source.Model?.Links[0].Id;
        }

        private string? GetTargetNodeId(LinkModel link)
        {
            return link.Target.Model?.Links[0].Id;
        }

        private GraphNode? FindNodeById(GraphLayout layout, string nodeId)
        {
            return layout.Nodes.FirstOrDefault(n => n.Id == nodeId);
        }

        #endregion
    }
}
