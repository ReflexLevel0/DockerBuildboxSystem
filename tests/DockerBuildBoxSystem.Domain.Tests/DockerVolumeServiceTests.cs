using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using NSubstitute;

namespace DockerBuildBoxSystem.Domain.Tests
{
    public class DockerVolumeServiceTests
    {
        private static DockerVolumeService CreateDockerVolumeService(IDockerClient dockerClient)
        {
            return new DockerVolumeService(dockerClient);
        }

        [Fact]
        public async Task CreateVolumeAsync()
        {
            // Setup
            var dockerClient = Substitute.For<IDockerClient>();
            var volumeParameters = new VolumesCreateParameters() { Name = "TestVolume" };
            var sharedVolume = new VolumeResponse() { Name = "TestVolume" };
            dockerClient.Volumes
                .CreateAsync(volumeParameters)
                .Returns(_ => Task.FromResult(sharedVolume));
            var volumeService = CreateDockerVolumeService(dockerClient);

            // Action
            var actualSharedVolume = await volumeService.CreateVolumeAsync(volumeParameters);
            
            // Validation
            Assert.Equal(sharedVolume, actualSharedVolume);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetSharedVolumeAsync_VolumeExists(bool createSharedVolumeIfNotExists)
        {
            //Setup
            var dockerClient = Substitute.For<IDockerClient>();
            var sharedVolume = new VolumeResponse() { Name = DockerVolumeService.SharedVolumeName };
            var volumeList = new VolumesListResponse() { Volumes = [ sharedVolume ]};
            dockerClient.Volumes
                .ListAsync()
                .Returns(Task.FromResult(volumeList));
            var volumeService = CreateDockerVolumeService(dockerClient);

            // Action
            var actualSharedVolume = await volumeService.GetSharedVolumeAsync(createSharedVolumeIfNotExists);

            // Validation
            Assert.Equal(sharedVolume, actualSharedVolume);
            await dockerClient.Volumes.DidNotReceiveWithAnyArgs().CreateAsync(null);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetSharedVolumeAsync_VolumeDoesntExist(bool createSharedVolumeIfNotExists)
        {
            //Setup
            var dockerClient = Substitute.For<IDockerClient>();
            var testVolume = new VolumeResponse() { Name = "BlaBlaTestVolume" };
            var volumeList = new VolumesListResponse() { Volumes = [testVolume] };
            dockerClient.Volumes
                .ListAsync()
                .Returns(Task.FromResult(volumeList));
            var sharedVolume = new VolumeResponse() { Name = DockerVolumeService.SharedVolumeName };

            // this should be tested with Returns instead of ReturnsForAnyArgs ideally
            // but there is no way to test if 2 VolumsCreateParameters are the same
            // because its an external class
            dockerClient.Volumes
                .CreateAsync(new VolumesCreateParameters() { Name = DockerVolumeService.SharedVolumeName })
                .ReturnsForAnyArgs(Task.FromResult(sharedVolume));

            var volumeService = CreateDockerVolumeService(dockerClient);

            // Action
            var actualSharedVolume = await volumeService.GetSharedVolumeAsync(createSharedVolumeIfNotExists);

            // Validation
            if(createSharedVolumeIfNotExists)
            {
                Assert.Equal(sharedVolume, actualSharedVolume);
            }
            else
            {
                Assert.Null(actualSharedVolume);
            }
        }
    }
}
