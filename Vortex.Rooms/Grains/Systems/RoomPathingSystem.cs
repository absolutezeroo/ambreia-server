using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Rooms.Mapping;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;

namespace Vortex.Rooms.Grains.Systems;

public sealed class RoomPathingSystem(RoomGrain roomGrain)
{
    /// <summary>
    /// How many surfaces a tile is assumed to be able to present, for the purpose of never budgeting
    /// a search below what its own room needs. Not a limit on anything — a tile with more of them
    /// simply eats into the slack, and RoomConfig.MaxPathNodes is still the ceiling when it is
    /// higher than this floor.
    /// </summary>
    private const int NodesPerTileFloor = 3;

    private static readonly int CARDINAL_COST = 10;
    private static readonly int DIAGONAL_COST = 14;

    private static readonly (int dx, int dy, int cost)[] DIRECTIONS =
    {
        (0, -1, 10), // N
        (1, -1, 14), // NE
        (1, 0, 10), // E
        (1, 1, 14), // SE
        (0, 1, 10), // S
        (-1, 1, 14), // SW
        (-1, 0, 10), // W
        (-1, -1, 14), // NW
    };

    private readonly RoomGrain _roomGrain = roomGrain;

    public IReadOnlyList<(int X, int Y)> FindPath(
        IRoomAvatar avatar,
        (int X, int Y) start,
        (int X, int Y) goal,
        int? targetZKey = null
    )
    {
        // The avatar's own altitude, not the tile's highest surface. Reading the top instead told
        // the search that somebody standing *under* a platform was standing *on* it, so every
        // neighbouring floor tile was three units below the foot it thought it had, no step was
        // within reach, and the walk came back empty: you could get under a piece of furniture and
        // then not get out again.
        return FindPath(
            start,
            goal,
            avatar.Z,
            targetZKey,
            tileIdx => _roomGrain.MapModule.CanAvatarWalk(avatar, tileIdx),
            (currentTileId, nextTileId, fromZ, isGoal) =>
                _roomGrain.MapModule.GetWalkableSectionsBetween(
                    avatar,
                    currentTileId,
                    nextTileId,
                    fromZ,
                    isGoal
                )
        );
    }

    /// <summary>
    /// The flat search, for callers that have no height model.
    ///
    /// Nothing calls it yet. It is for a walker with its own rule about which tiles it may occupy
    /// and nowhere to remember which surface it is standing on, for which the avatars'
    /// three-dimensional search does not fit. Every step lands on the tile's top surface, and the
    /// caller's predicates are passed through untouched.
    /// </summary>
    public IReadOnlyList<(int X, int Y)> FindPath(
        (int X, int Y) start,
        (int X, int Y) goal,
        Func<int, bool> canOccupyTile,
        Func<int, int, bool, bool> canMoveBetween
    )
    {
        return FindPath(
            start,
            goal,
            _roomGrain.MapModule.GetTopSection(_roomGrain.MapModule.ToIdx(start.X, start.Y)).Height,
            null,
            canOccupyTile,
            (currentTileId, nextTileId, _, isGoal) =>
                canMoveBetween(currentTileId, nextTileId, isGoal)
                    ? [_roomGrain.MapModule.GetTopSection(nextTileId)]
                    : []
        );
    }

    /// <summary>Every surface a step onto the next tile could land on; empty when the step cannot
    /// be taken at all.</summary>
    internal delegate List<RoomTileSection> StepsBetween(
        int currentTileId,
        int nextTileId,
        Altitude fromZ,
        bool isGoal
    );

    internal IReadOnlyList<(int X, int Y)> FindPath(
        (int X, int Y) start,
        (int X, int Y) goal,
        Altitude startZ,
        int? targetZKey,
        Func<int, bool> canOccupyTile,
        StepsBetween stepsBetween
    )
    {
        try
        {
            (int startX, int startY) = start;
            (int goalX, int goalY) = goal;
            int currentTileId = _roomGrain.MapModule.ToIdx(start.X, start.Y);
            int goalTileId = _roomGrain.MapModule.ToIdx(goal.X, goal.Y);

            if (!canOccupyTile(currentTileId) || !canOccupyTile(goalTileId))
            {
                return [];
            }

            // Walking to the tile you are on is a real request now: its other surface. Standing
            // still is not an answer to it, so the start's own height is barred as an arrival and
            // the search has to leave and come back at a different one.
            int? barredArrivalZ = currentTileId == goalTileId ? ZKeyOf(startZ) : null;

            PriorityQueue<Node, int> open = new();
            Dictionary<(int, int, int), Node> allNodes = new(256);

            // A tile is not one place any more. Standing under a platform and standing on it are
            // different nodes of the same (x, y), reached by different routes and at different
            // costs, so the altitude is part of the key -- quantised to hundredths, which is the
            // resolution altitudes are authored and sent at, because a double is no kind of
            // dictionary key.
            //
            // Keying by (x, y) alone is what made clicking a raised item from below walk you to the
            // floor underneath it instead: the tile was reached at floor level first, closed at
            // that height, and the approach along the stairs then found it already visited.
            static int ZKey(Altitude z) => ZKeyOf(z);

            Node GetOrCreateNode(int x, int y, Altitude z)
            {
                (int x, int y, int) key = (x, y, ZKey(z));

                if (allNodes.TryGetValue(key, out Node? n))
                {
                    return n;
                }

                n = new Node
                {
                    X = x,
                    Y = y,
                    Z = z,
                };
                allNodes[key] = n;

                return n;
            }

            Node startNode = GetOrCreateNode(startX, startY, startZ);

            startNode.G = 0;
            startNode.H = Heuristic(startX, startY, goalX, goalY);
            startNode.Parent = null;

            open.Enqueue(startNode, startNode.F);

            HashSet<(int, int, int)> closed = new();
            Node? best = null;

            // Never below what the map itself needs. The configured value is a ceiling on a runaway
            // search, and at its 4,096 default it was under the tile count of a large room (76x76 is
            // 5,776) -- so the search ran out of budget before it had looked at the room once, and
            // the walk simply did not happen. Nodes are now per *surface* rather than per tile, so
            // the floor allows a few of them each: one tile can be a floor, the top of a platform,
            // and the crawlspace between.
            int maxNodes = Math.Max(
                _roomGrain._roomConfig.MaxPathNodes,
                _roomGrain.MapModule.Width * _roomGrain.MapModule.Height * NodesPerTileFloor
            );

            while (open.Count > 0 && allNodes.Count <= maxNodes)
            {
                try
                {
                    Node current = open.Dequeue();
                    (int X, int Y, int Z) cKey = (current.X, current.Y, ZKey(current.Z));
                    int cTileId = _roomGrain.MapModule.ToIdx(current.X, current.Y);

                    if (!closed.Add(cKey))
                    {
                        continue;
                    }

                    if (
                        current.X == goalX
                        && current.Y == goalY
                        && ZKey(current.Z) != barredArrivalZ
                    )
                    {
                        // Not "first arrival wins" any more, because a tile now has more than one
                        // place to arrive at and the cheapest of them is the wrong one: clicking a
                        // raised item from the floor would walk you into the crawlspace underneath
                        // it, which is nearer than its top and is not what anyone means by clicking
                        // a thing. A click means the highest surface that can be reached.
                        //
                        // A* still stops early -- on the first arrival that cannot be beaten. Every
                        // node still queued costs at least the F at the head, so once that passes
                        // the best arrival's cost nothing better can turn up and the search ends
                        // there rather than walking the whole room.
                        // "Highest wins" was a stand-in for a height the wire did not carry. When
                        // the client names the surface it clicked, that beats the guess outright --
                        // clicking the floor *under* a platform now walks there instead of onto it.
                        // The guess stays for a client that sends no height, and for the case where
                        // the named surface turns out to be unreachable.
                        bool wanted = targetZKey is not null && ZKey(current.Z) == targetZKey;
                        bool bestWanted = best is not null && ZKey(best.Z) == targetZKey;

                        if (
                            best is null
                            || (wanted && !bestWanted)
                            || (wanted == bestWanted && current.Z > best.Z)
                        )
                        {
                            best = current;
                            bestWanted = wanted;
                        }

                        // Only stop early on an arrival that is actually the one asked for. With a
                        // requested surface still unfound, a cheaper wrong-height arrival must not
                        // end the search that would have reached it.
                        if (
                            (bestWanted || targetZKey is null)
                            && (open.Count == 0 || open.Peek().F > best.F)
                        )
                        {
                            return ReconstructPath(best);
                        }

                        // No `continue`: a goal node is expanded like any other. It has to be, for
                        // the barred case above -- the avatar starts *on* the goal tile, at the one
                        // height that is not an answer, and if reaching it ended the node's turn the
                        // search would never leave the tile it is trying to leave.
                    }

                    for (int i = 0; i < DIRECTIONS.Length; i++)
                    {
                        try
                        {
                            (int dx, int dy, int moveCost) = DIRECTIONS[i];
                            int nx = current.X + dx;
                            int ny = current.Y + dy;

                            if (
                                nx < 0
                                || ny < 0
                                || nx >= _roomGrain.MapModule.Width
                                || ny >= _roomGrain.MapModule.Height
                            )
                            {
                                continue;
                            }

                            int nTileId = _roomGrain.MapModule.ToIdx(nx, ny);

                            int tentativeG = current.G + moveCost;

                            // One neighbour, several nodes: a tile with two surfaces is two places
                            // to arrive at, reached at different costs and leading on to different
                            // steps. Offering only the best of them is what made the walk one-way
                            // -- always able to climb onto a platform, never able to step back down
                            // off it, because the higher surface was the only one ever proposed.
                            foreach (
                                RoomTileSection landing in stepsBetween(
                                    cTileId,
                                    nTileId,
                                    current.Z,
                                    nx == goalX && ny == goalY
                                )
                            )
                            {
                                if (closed.Contains((nx, ny, ZKey(landing.Height))))
                                {
                                    continue;
                                }

                                // The start *node*, not the start tile. Those stopped being the
                                // same thing when the key gained an altitude: coming back to the
                                // tile you set off from at a different height is a real step, and
                                // the only way to reach the other surface of the tile you are on.
                                Node neighbor = GetOrCreateNode(nx, ny, landing.Height);

                                if (
                                    neighbor.Parent == null
                                    && !ReferenceEquals(neighbor, startNode)
                                )
                                {
                                    neighbor.Parent = current;
                                    neighbor.G = tentativeG;
                                    neighbor.H = Heuristic(nx, ny, goalX, goalY);
                                    open.Enqueue(neighbor, neighbor.F);
                                }
                                else if (tentativeG < neighbor.G)
                                {
                                    neighbor.Parent = current;
                                    neighbor.G = tentativeG;
                                    open.Enqueue(neighbor, neighbor.F);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _roomGrain._logger.LogWarning(
                                ex,
                                "Failed to evaluate pathfinding neighbor from ({X},{Y}) direction {DirectionIndex} in room {RoomId}.",
                                current.X,
                                current.Y,
                                i,
                                _roomGrain.RoomId
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    _roomGrain._logger.LogWarning(
                        ex,
                        "Failed to expand pathfinding node while searching from ({StartX},{StartY}) to ({GoalX},{GoalY}) in room {RoomId}.",
                        start.X,
                        start.Y,
                        goal.X,
                        goal.Y,
                        _roomGrain.RoomId
                    );
                }
            }

            // Exhausted, or out of node budget. An arrival found along the way is still the answer.
            if (best is not null)
            {
                return ReconstructPath(best);
            }

            // Saturation and "there is genuinely no way there" both came back as an empty path, and
            // an empty path is how a walk quietly does not happen. They are not the same problem and
            // only one of them is a bug in this method, so say which.
            if (allNodes.Count > maxNodes)
            {
                _roomGrain._logger.LogWarning(
                    "Pathfinding from ({StartX},{StartY}) to ({GoalX},{GoalY}) in room {RoomId} ran "
                        + "out of nodes at {MaxNodes}; no path was returned. The room may be larger "
                        + "than RoomConfig.MaxPathNodes allows for.",
                    start.X,
                    start.Y,
                    goal.X,
                    goal.Y,
                    _roomGrain.RoomId,
                    maxNodes
                );
            }
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to find path from ({StartX},{StartY}) to ({GoalX},{GoalY}) in room {RoomId}.",
                start.X,
                start.Y,
                goal.X,
                goal.Y,
                _roomGrain.RoomId
            );
        }

        return [];
    }

    /// <summary>An altitude as a dictionary key: hundredths, the resolution altitudes are
    /// authored and sent at. A double is no kind of key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ZKeyOf(Altitude z) => (int)Math.Round(z * 100);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Heuristic(int x, int y, int goalX, int goalY)
    {
        int dx = Math.Abs(x - goalX);
        int dy = Math.Abs(y - goalY);

        return dx < dy
            ? DIAGONAL_COST * dx + CARDINAL_COST * (dy - dx)
            : DIAGONAL_COST * dy + CARDINAL_COST * (dx - dy);
    }

    private static List<(int X, int Y)> ReconstructPath(Node goalNode)
    {
        List<(int, int)> list = new();
        Node? current = goalNode;

        while (current != null)
        {
            list.Add((current.X, current.Y));
            current = current.Parent!;
        }

        list.Reverse();

        return list;
    }

    internal sealed class Node
    {
        public int G; // Cost from start
        public int H; // Heuristic cost to goal
        public Node? Parent;
        public int X;
        public int Y;

        /// <summary>
        /// The altitude the walk is at when it stands here — the surface it stepped onto, not the
        /// tile's highest.
        ///
        /// It is what the next step is measured from, and it is why a route can pass under a raised
        /// platform: the neighbour search asks the map which of that tile's surfaces is within a
        /// step of *this* altitude, so the floor is offered to a walk along the floor and the roof
        /// to a walk along the roof.
        /// </summary>
        public Altitude Z;

        public int F => G + H;
    }
}
