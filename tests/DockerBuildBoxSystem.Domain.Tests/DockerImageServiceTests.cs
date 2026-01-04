using Docker.DotNet;
using Docker.DotNet.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain.Tests
{
    public class DockerImageServiceTests
    {
        private static DockerImageService CreateDockerImageService(IDockerClient client) =>
            new DockerImageService(client);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InspectImageAsync(bool imageExists)
        {
            // Setup
            var client = Substitute.For<IDockerClient>();
            var imageReponse = new ImageInspectResponse()
            {
                ID = "someImageId",
                RepoTags = new List<string> { "a", "b", "c" },
                Created = new DateTime(2000, 1, 1),
                Size = 100,
                VirtualSize = 200,
                Config = new() { Labels = new Dictionary<string, string> { { "d", "e" }, { "f", "g" } } }
            };

            if(imageExists)
            {
                client.Images.InspectImageAsync(imageReponse.ID).Returns(Task.FromResult(imageReponse));
            }else
            {
                client.Images.InspectImageAsync(imageReponse.ID).ThrowsAsync(new DockerImageNotFoundException(HttpStatusCode.NotFound, ""));
            }

            // Act
            var image = await CreateDockerImageService(client).InspectImageAsync(imageReponse.ID);

            // Validation
            if (imageExists)
            {
                Assert.True(string.CompareOrdinal(imageReponse.ID, image.Id) == 0);
                Assert.True(
                    image.RepoTags.Count == 3 &&
                    image.RepoTags.All(t1 => imageReponse.RepoTags.Any(t2 => string.CompareOrdinal(t1, t2) == 0))
                );
                Assert.Equal(new DateTime(2000, 1, 1), image.Created);
                Assert.Equal(100, image.Size);
                Assert.Equal(200, image.VirtualSize);
                Assert.Equal(imageReponse.Config.Labels, image.Labels);
            } else
            {
                Assert.Null(image);
            }
        }

        [Fact]
        public async Task ListImagesAsync()
        {
            // Setup
            var client = Substitute.For<IDockerClient>();
            var imageReponse = new ImagesListResponse()
            {
                ID = "someImageId",
                RepoTags = new List<string> { "a", "b", "c" },
                Created = new DateTime(2000, 1, 1),
                Size = 100,
                VirtualSize = 200,
                Labels = new Dictionary<string, string> { { "d", "e" }, { "f", "g" } }
            };
            client.Images.ListImagesAsync(null).ReturnsForAnyArgs(new List<ImagesListResponse>() { imageReponse });

            // Act
            var images = await CreateDockerImageService(client).ListImagesAsync();

            // Validation
            Assert.True(images.Count == 1);
            var image = images[0];
            Assert.True(string.CompareOrdinal(imageReponse.ID, image.Id) == 0);
            Assert.True(
                image.RepoTags.Count == 3 &&
                image.RepoTags.All(t1 => imageReponse.RepoTags.Any(t2 => string.CompareOrdinal(t1, t2) == 0))
            );
            Assert.Equal(new DateTime(2000, 1, 1), image.Created);
            Assert.Equal(100, image.Size);
            Assert.Equal(200, image.VirtualSize);
            Assert.Equal(imageReponse.Labels, image.Labels);
        }
    }
}
